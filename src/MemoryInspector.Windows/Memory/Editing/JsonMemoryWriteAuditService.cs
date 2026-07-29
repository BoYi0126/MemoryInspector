using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Windows.Memory.Editing;

public sealed class JsonMemoryWriteAuditService(
    IAppPathService pathService) :
    IMemoryWriteAuditService,
    IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow,
        };
    private readonly IAppPathService _pathService =
        Guard.NotNull(pathService);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public async Task<Result> RecordAsync(
        MemoryWriteAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var wait = await WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        if (wait.IsFailure)
        {
            return wait;
        }

        string? temporaryPath = null;

        try
        {
            var directories = _pathService.EnsureDirectories();

            if (directories.IsFailure)
            {
                return directories;
            }

            var fileName =
                $"{entry.Timestamp.UtcTicks:D19}-" +
                $"{entry.AuditId:N}.json";
            var path = Path.Combine(
                _pathService.MemoryEditorAuditDirectory,
                fileName);
            temporaryPath = $"{path}.tmp";
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4_096,
                FileOptions.Asynchronous |
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        FromModel(entry),
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path);
            temporaryPath = null;
            return Result.Success();
        }
        catch (Exception exception)
        {
            return Failure(
                exception,
                "Memory Editor audit entry could not be recorded.",
                cancellationToken);
        }
        finally
        {
            TryDelete(temporaryPath);
            _gate.Release();
        }
    }

    public async Task<Result<IReadOnlyList<MemoryWriteAuditEntry>>>
        ReadRecentAsync(
            int maximumCount = 1_000,
            CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0)
        {
            return Result<IReadOnlyList<MemoryWriteAuditEntry>>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Audit maximum count must be greater than zero."));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        var wait = await WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        if (wait.IsFailure)
        {
            return Result<IReadOnlyList<MemoryWriteAuditEntry>>.Failure(
                wait.Error);
        }

        try
        {
            var directories = _pathService.EnsureDirectories();

            if (directories.IsFailure)
            {
                return Result<
                    IReadOnlyList<MemoryWriteAuditEntry>>.Failure(
                        directories.Error);
            }

            var paths = Directory
                .EnumerateFiles(
                    _pathService.MemoryEditorAuditDirectory,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(
                    path => Path.GetFileName(path),
                    StringComparer.Ordinal)
                .Take(maximumCount)
                .ToArray();
            var entries = new List<MemoryWriteAuditEntry>(
                paths.Length);

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4_096,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan);
                var document = await JsonSerializer
                    .DeserializeAsync<AuditDocument>(
                        stream,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (document is null)
                {
                    throw new InvalidDataException(
                        $"Audit file '{path}' is empty.");
                }

                entries.Add(ToModel(document));
            }

            return Result<
                IReadOnlyList<MemoryWriteAuditEntry>>.Success(
                    Array.AsReadOnly(entries.ToArray()));
        }
        catch (Exception exception)
        {
            return Failure<IReadOnlyList<MemoryWriteAuditEntry>>(
                exception,
                "Memory Editor audit entries could not be read.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private async Task<Result> WaitAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Memory Editor audit operation was cancelled.",
                    exception));
        }
    }

    private static AuditDocument FromModel(
        MemoryWriteAuditEntry entry)
    {
        return new AuditDocument
        {
            SchemaVersion = 1,
            AuditId = entry.AuditId,
            SessionId = entry.SessionId,
            ProcessId = entry.TargetIdentity.ProcessId,
            ProcessName = entry.TargetIdentity.ProcessName,
            ProcessStartTime =
                entry.TargetIdentity.ProcessStartTime,
            Architecture =
                entry.TargetIdentity.Architecture.ToString(),
            Address = $"0x{entry.Address:X16}",
            ValueType = entry.ValueType.ToString(),
            OriginalValue = ToHex(entry.OriginalValue),
            RequestedValue =
                Convert.ToHexString(entry.RequestedValue.Span),
            ReadBackValue = ToHex(entry.ReadBackValue),
            Success = entry.Success,
            VerificationStatus =
                entry.VerificationStatus.ToString(),
            FailureReason = entry.FailureReason.ToString(),
            ErrorCode = entry.ErrorCode.ToString(),
            ErrorMessage = entry.ErrorMessage,
            Timestamp = entry.Timestamp,
            Source = entry.Source.ToString(),
            UserNote = entry.UserNote,
        };
    }

    private static MemoryWriteAuditEntry ToModel(
        AuditDocument document)
    {
        if (document.SchemaVersion != 1 ||
            !Enum.TryParse<ProcessArchitecture>(
                document.Architecture,
                out var architecture) ||
            architecture == ProcessArchitecture.Unknown ||
            !Enum.TryParse<ScanValueType>(
                document.ValueType,
                out var valueType) ||
            !Enum.TryParse<MemoryWriteVerificationStatus>(
                document.VerificationStatus,
                out var verification) ||
            !Enum.TryParse<MemoryWriteFailureReason>(
                document.FailureReason,
                out var failureReason) ||
            !Enum.TryParse<ErrorCode>(
                document.ErrorCode,
                out var errorCode) ||
            !Enum.TryParse<MemoryWriteSource>(
                document.Source,
                out var source))
        {
            throw new InvalidDataException(
                "Memory Editor audit metadata is invalid.");
        }

        var requested = ParseHex(document.RequestedValue);
        var original = ParseOptionalHex(document.OriginalValue);
        var readBack = ParseOptionalHex(document.ReadBackValue);
        return new MemoryWriteAuditEntry(
            document.AuditId,
            document.SessionId,
            new MonitoringSessionIdentity(
                document.ProcessId,
                document.ProcessStartTime,
                architecture,
                document.ProcessName ??
                    throw new InvalidDataException(
                        "Audit process name is required.")),
            ParseAddress(document.Address),
            valueType,
            original,
            requested,
            readBack,
            document.Success,
            verification,
            failureReason,
            errorCode,
            document.ErrorMessage,
            document.Timestamp,
            source,
            document.UserNote);
    }

    private static ulong ParseAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !text.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase) ||
            !ulong.TryParse(
                text[2..],
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var address))
        {
            throw new InvalidDataException(
                "Audit address is invalid.");
        }

        return address;
    }

    private static string? ToHex(
        ReadOnlyMemory<byte>? value)
    {
        return value.HasValue
            ? Convert.ToHexString(value.Value.Span)
            : null;
    }

    private static ReadOnlyMemory<byte>? ParseOptionalHex(
        string? value)
    {
        return value is null
            ? default(ReadOnlyMemory<byte>?)
            : new ReadOnlyMemory<byte>(ParseHex(value));
    }

    private static byte[] ParseHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                "Audit byte data is required.");
        }

        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "Audit byte data is invalid.",
                exception);
        }
    }

    private static Result Failure(
        Exception exception,
        string message,
        CancellationToken cancellationToken)
    {
        return Result.Failure(CreateError(
            exception,
            message,
            cancellationToken));
    }

    private static Result<T> Failure<T>(
        Exception exception,
        string message,
        CancellationToken cancellationToken)
    {
        return Result<T>.Failure(CreateError(
            exception,
            message,
            cancellationToken));
    }

    private static Error CreateError(
        Exception exception,
        string message,
        CancellationToken cancellationToken)
    {
        return exception is OperationCanceledException &&
               cancellationToken.IsCancellationRequested
            ? new Error(ErrorCode.Cancelled, message, exception)
            : exception is JsonException or
                InvalidDataException or
                FormatException
                ? new Error(
                    ErrorCode.Serialization,
                    message,
                    exception)
                : new Error(ErrorCode.Io, message, exception);
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  NotSupportedException)
        {
        }
    }

    private sealed class AuditDocument
    {
        public int SchemaVersion { get; init; }
        public Guid AuditId { get; init; }
        public Guid SessionId { get; init; }
        public int ProcessId { get; init; }
        public string? ProcessName { get; init; }
        public DateTimeOffset ProcessStartTime { get; init; }
        public string? Architecture { get; init; }
        public string? Address { get; init; }
        public string? ValueType { get; init; }
        public string? OriginalValue { get; init; }
        public string? RequestedValue { get; init; }
        public string? ReadBackValue { get; init; }
        public bool Success { get; init; }
        public string? VerificationStatus { get; init; }
        public string? FailureReason { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTimeOffset Timestamp { get; init; }
        public string? Source { get; init; }
        public string? UserNote { get; init; }
    }
}

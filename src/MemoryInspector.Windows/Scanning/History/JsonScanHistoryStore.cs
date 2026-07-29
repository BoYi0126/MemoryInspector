using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Scanning.History;
using MemoryInspector.Common;

namespace MemoryInspector.Windows.Scanning.History;

public sealed class JsonScanHistoryStore(
    IAppPathService pathService) : IScanHistoryStore, IDisposable
{
    private const string HistoryFileName = "tree.json";
    private const string LegacyHistoryFileName =
        "scan-history.json";
    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow,
        };
    private readonly IAppPathService _pathService =
        Guard.NotNull(pathService);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public async Task<Result<ScanHistoryDocument>> LoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Validation<ScanHistoryDocument>(
                "Session ID cannot be empty.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _gate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            return Cancelled<ScanHistoryDocument>(
                "Loading scan history was cancelled.",
                exception);
        }

        try
        {
            var path = GetHistoryPath(sessionId);

            if (!File.Exists(path))
            {
                path = GetLegacyHistoryPath(sessionId);
            }

            if (!File.Exists(path))
            {
                return Result<ScanHistoryDocument>.Failure(
                    new Error(
                        ErrorCode.NotFound,
                        "Scan history was not found."));
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);
            var document =
                await JsonSerializer
                    .DeserializeAsync<ScanHistoryDocument>(
                        stream,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (document is null ||
                document.SessionId != sessionId)
            {
                throw new InvalidDataException(
                    "Scan history identity is invalid.");
            }

            return Result<ScanHistoryDocument>.Success(document);
        }
        catch (Exception exception)
        {
            return Failure<ScanHistoryDocument>(
                exception,
                "Scan history could not be loaded.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> SaveAsync(
        ScanHistoryDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document is null)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "A scan history document is required."));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _gate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            return Cancelled(
                "Saving scan history was cancelled.",
                exception);
        }

        string? temporaryPath = null;

        try
        {
            var directoryResult = _pathService.EnsureDirectories();

            if (directoryResult.IsFailure)
            {
                return directoryResult;
            }

            var sessionDirectory = GetSessionDirectory(
                document.SessionId);
            Directory.CreateDirectory(sessionDirectory);
            var path = GetHistoryPath(document.SessionId);
            temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous |
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        document,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(
                temporaryPath,
                path,
                overwrite: true);
            temporaryPath = null;
            return Result.Success();
        }
        catch (Exception exception)
        {
            return Failure(
                exception,
                "Scan history could not be saved.",
                cancellationToken);
        }
        finally
        {
            if (temporaryPath is not null &&
                File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

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

    private string GetSessionDirectory(Guid sessionId)
    {
        return Path.Combine(
            _pathService.TempDirectory,
            sessionId.ToString("D"));
    }

    private string GetHistoryPath(Guid sessionId)
    {
        return Path.Combine(
            GetSessionDirectory(sessionId),
            HistoryFileName);
    }

    private string GetLegacyHistoryPath(Guid sessionId)
    {
        return Path.Combine(
            _pathService.SessionsDirectory,
            sessionId.ToString("D"),
            LegacyHistoryFileName);
    }

    private static Result<T> Failure<T>(
        Exception exception,
        string message,
        CancellationToken cancellationToken)
    {
        return exception switch
        {
            OperationCanceledException
                when cancellationToken.IsCancellationRequested =>
                Cancelled<T>(message, exception),
            JsonException or
            InvalidDataException or
            ArgumentException or
            InvalidOperationException =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.Serialization,
                        message,
                        exception)),
            IOException or
            UnauthorizedAccessException or
            NotSupportedException =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.Io,
                        message,
                        exception)),
            _ => Result<T>.Failure(
                new Error(
                    ErrorCode.Unexpected,
                    message,
                    exception)),
        };
    }

    private static Result Failure(
        Exception exception,
        string message,
        CancellationToken cancellationToken)
    {
        var result = Failure<object>(
            exception,
            message,
            cancellationToken);
        return Result.Failure(result.Error);
    }

    private static Result<T> Validation<T>(string message)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Validation,
                message));
    }

    private static Result<T> Cancelled<T>(
        string message,
        Exception? exception = null)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Cancelled,
                message,
                exception));
    }

    private static Result Cancelled(
        string message,
        Exception? exception = null)
    {
        return Result.Failure(
            new Error(
                ErrorCode.Cancelled,
                message,
                exception));
    }
}

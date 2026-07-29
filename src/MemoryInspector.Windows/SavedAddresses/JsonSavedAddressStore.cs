using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.SavedAddresses;
using MemoryInspector.Common;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Windows.SavedAddresses;

public sealed class JsonSavedAddressStore(
    IAppPathService pathService) : ISavedAddressStore
{
    private const string DefaultFileName =
        "saved-addresses.json";
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

    public string DefaultFilePath => Path.Combine(
        _pathService.SavedAddressesDirectory,
        DefaultFileName);

    public async Task<Result<SavedAddressCatalog>> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Validation<SavedAddressCatalog>(
                "A saved-address file path is required.");
        }

        var path = Path.GetFullPath(filePath);

        if (!File.Exists(path))
        {
            return Result<SavedAddressCatalog>.Failure(
                new Error(
                    ErrorCode.NotFound,
                    $"Saved-address file '{path}' was not found."));
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);
            var document = await JsonSerializer
                .DeserializeAsync<SavedAddressJsonDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                throw new InvalidDataException(
                    "The saved-address JSON document is empty.");
            }

            return Result<SavedAddressCatalog>.Success(
                ToCatalog(document));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled<SavedAddressCatalog>(
                "Loading saved addresses was cancelled.",
                exception);
        }
        catch (Exception exception)
            when (exception is JsonException or
                  InvalidDataException or
                  ArgumentException)
        {
            return Result<SavedAddressCatalog>.Failure(
                new Error(
                    ErrorCode.Serialization,
                    "The saved-address JSON format is invalid.",
                    exception));
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  NotSupportedException)
        {
            return Io<SavedAddressCatalog>(
                "Saved addresses could not be read.",
                exception);
        }
    }

    public async Task<Result> SaveAsync(
        SavedAddressCatalog catalog,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Validation(
                "A saved-address file path is required.");
        }

        var path = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return Validation(
                "The saved-address file directory is invalid.");
        }

        string? temporaryPath = null;

        try
        {
            if (string.Equals(
                path,
                DefaultFilePath,
                StringComparison.OrdinalIgnoreCase))
            {
                var directories =
                    _pathService.EnsureDirectories();

                if (directories.IsFailure)
                {
                    return directories;
                }
            }
            else
            {
                Directory.CreateDirectory(directory);
            }

            temporaryPath =
                $"{path}.tmp-{Guid.NewGuid():N}";
            var document = FromCatalog(catalog);

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
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Saving saved addresses was cancelled.",
                    exception));
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  NotSupportedException)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Io,
                    "Saved addresses could not be written.",
                    exception));
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static SavedAddressCatalog ToCatalog(
        SavedAddressJsonDocument document)
    {
        if (document.SchemaVersion !=
            SavedAddressCatalog.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported saved-address schema version " +
                $"'{document.SchemaVersion}'.");
        }

        if (document.Addresses is null)
        {
            throw new InvalidDataException(
                "The addresses object is required.");
        }

        SavedAddressTarget? target = null;

        if (document.Target is not null)
        {
            if (!Enum.TryParse<ProcessArchitecture>(
                    document.Target.Architecture,
                    ignoreCase: true,
                    out var architecture) ||
                architecture == ProcessArchitecture.Unknown)
            {
                throw new InvalidDataException(
                    "The target architecture is invalid.");
            }

            target = new SavedAddressTarget(
                document.Target.ProcessName ??
                    throw new InvalidDataException(
                        "The target process name is required."),
                architecture);
        }

        var entries = new List<SavedAddressEntry>(
            document.Addresses.Count);

        foreach (var pair in document.Addresses)
        {
            if (pair.Value is null)
            {
                throw new InvalidDataException(
                    $"Address '{pair.Key}' must contain an object.");
            }

            entries.Add(
                new SavedAddressEntry(
                    pair.Key,
                    ParseAddress(pair.Value.Address),
                    ParseValueType(pair.Value.ValueType),
                    pair.Value.Description));
        }

        return new SavedAddressCatalog(target, entries);
    }

    private static SavedAddressJsonDocument FromCatalog(
        SavedAddressCatalog catalog)
    {
        var addresses = new SortedDictionary<
            string,
            SavedAddressJsonEntry?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalog.Entries)
        {
            addresses.Add(
                entry.Key,
                new SavedAddressJsonEntry
                {
                    Address = $"0x{entry.Address:X16}",
                    ValueType = entry.ValueType.ToString(),
                    Description = entry.Description,
                });
        }

        return new SavedAddressJsonDocument
        {
            SchemaVersion = SavedAddressCatalog.CurrentSchemaVersion,
            Target = catalog.Target is null
                ? null
                : new SavedAddressJsonTarget
                {
                    ProcessName = catalog.Target.ProcessName,
                    Architecture = catalog.Target.Architecture
                        .ToString()
                        .ToLowerInvariant(),
                },
            Addresses = addresses,
        };
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
                $"Saved address '{text}' is not a hexadecimal x64 address.");
        }

        return address;
    }

    private static ScanValueType ParseValueType(string? text)
    {
        if (!Enum.TryParse<ScanValueType>(
                text,
                ignoreCase: true,
                out var valueType) ||
            !Enum.IsDefined(valueType))
        {
            throw new InvalidDataException(
                $"Saved-address value type '{text}' is invalid.");
        }

        return valueType;
    }

    private static void TryDeleteTemporaryFile(string? path)
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

    private static Result Validation(string message)
    {
        return Result.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private static Result<T> Validation<T>(string message)
    {
        return Result<T>.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private static Result<T> Cancelled<T>(
        string message,
        Exception exception)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Cancelled,
                message,
                exception));
    }

    private static Result<T> Io<T>(
        string message,
        Exception exception)
    {
        return Result<T>.Failure(
            new Error(ErrorCode.Io, message, exception));
    }

    private sealed class SavedAddressJsonDocument
    {
        public int SchemaVersion { get; init; }

        public SavedAddressJsonTarget? Target { get; init; }

        public IDictionary<string, SavedAddressJsonEntry?>?
            Addresses { get; init; }
    }

    private sealed class SavedAddressJsonTarget
    {
        public string? ProcessName { get; init; }

        public string? Architecture { get; init; }
    }

    private sealed class SavedAddressJsonEntry
    {
        public string? Address { get; init; }

        public string? ValueType { get; init; }

        public string? Description { get; init; }
    }
}

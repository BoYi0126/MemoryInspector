using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Common;

namespace MemoryInspector.Windows.Configuration;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private readonly IAppPathService _pathService;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public JsonSettingsService(
        IAppPathService pathService,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _pathService = Guard.NotNull(pathService);
        _logger = Guard.NotNull(logger);
        _timeProvider = Guard.NotNull(timeProvider);
    }

    public async Task<Result<AppSettings>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var directoryResult = _pathService.EnsureDirectories();

        if (directoryResult.IsFailure)
        {
            return Result<AppSettings>.Failure(directoryResult.Error);
        }

        if (!File.Exists(_pathService.SettingsFilePath))
        {
            var defaults = AppSettings.CreateDefault();
            var saveResult = await SaveAsync(defaults, cancellationToken);

            if (saveResult.IsFailure)
            {
                return Result<AppSettings>.Failure(saveResult.Error);
            }

            _ = _logger.Log(
                AppLogLevel.Information,
                "Default application settings were created.");

            return Result<AppSettings>.Success(defaults);
        }

        try
        {
            await using var stream = new FileStream(
                _pathService.SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4_096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (settings is null)
            {
                throw new InvalidDataException(
                    "The settings document did not contain an object.");
            }

            var validationResult = settings.Validate();

            if (validationResult.IsFailure)
            {
                throw new InvalidDataException(
                    validationResult.Error.ToDisplayMessage(),
                    validationResult.Error.Exception);
            }

            return Result<AppSettings>.Success(settings);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException)
        {
            return await RecoverFromInvalidSettingsAsync(
                exception,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<AppSettings>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Loading application settings was cancelled.",
                    exception));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return Result<AppSettings>.Failure(
                new Error(
                    ErrorCode.Io,
                    "Application settings could not be read.",
                    exception));
        }
    }

    public async Task<Result> SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(settings);

        var validationResult = settings.Validate();

        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        var directoryResult = _pathService.EnsureDirectories();

        if (directoryResult.IsFailure)
        {
            return directoryResult;
        }

        var temporaryPath = _pathService.SettingsFilePath + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4_096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(
                temporaryPath,
                _pathService.SettingsFilePath,
                overwrite: true);

            return Result.Success();
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Saving application settings was cancelled.",
                    exception));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            _ = _logger.Log(
                AppLogLevel.Error,
                "Application settings could not be saved.",
                exception);

            return Result.Failure(
                new Error(
                    ErrorCode.Io,
                    "Application settings could not be saved.",
                    exception));
        }
    }

    private async Task<Result<AppSettings>> RecoverFromInvalidSettingsAsync(
        Exception exception,
        CancellationToken cancellationToken)
    {
        var timestamp = _timeProvider
            .GetLocalNow()
            .ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var invalidSettingsPath =
            $"{_pathService.SettingsFilePath}.corrupt.{timestamp}";

        try
        {
            File.Move(
                _pathService.SettingsFilePath,
                invalidSettingsPath,
                overwrite: false);
        }
        catch (Exception moveException) when (
            moveException is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            var moveError = new Error(
                ErrorCode.Io,
                "The invalid settings file could not be isolated.",
                moveException,
                new Error(
                    ErrorCode.Serialization,
                    "The application settings file is invalid.",
                    exception));

            _ = _logger.Log(
                AppLogLevel.Error,
                moveError.ToDisplayMessage(),
                moveException);

            return Result<AppSettings>.Failure(moveError);
        }

        var defaults = AppSettings.CreateDefault();
        var saveResult = await SaveAsync(defaults, cancellationToken);

        if (saveResult.IsFailure)
        {
            return Result<AppSettings>.Failure(saveResult.Error);
        }

        _ = _logger.Log(
            AppLogLevel.Warning,
            $"Invalid application settings were moved to '{invalidSettingsPath}'. " +
            "Default settings are now active.",
            exception);

        return Result<AppSettings>.Success(defaults);
    }
}

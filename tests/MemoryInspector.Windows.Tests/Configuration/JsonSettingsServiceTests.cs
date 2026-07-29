using System.Text.Json;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Windows.Configuration;

namespace MemoryInspector.Windows.Tests.Configuration;

[TestClass]
public sealed class JsonSettingsServiceTests
{
    [TestMethod]
    public async Task FirstLoadCreatesDefaultSettingsFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(
            Path.Combine(temporaryDirectory.RootPath, "AppData"));
        var logger = new RecordingLogger();
        var service = new JsonSettingsService(
            paths,
            logger,
            TimeProvider.System);

        var result = await service.LoadAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AppSettings.CreateDefault(), result.Value);
        Assert.IsTrue(File.Exists(paths.SettingsFilePath));
        Assert.IsTrue(
            logger.Entries.Any(entry =>
                entry.Level == AppLogLevel.Information));

        var json = await File.ReadAllTextAsync(paths.SettingsFilePath);
        Assert.IsTrue(json.Contains("\"schemaVersion\": 1", StringComparison.Ordinal));
        Assert.IsTrue(
            json.Contains(
                "\"memoryEditor\"",
                StringComparison.Ordinal));
        Assert.IsTrue(
            json.Contains(
                "\"enabled\": false",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SaveAndLoadRoundTripsValidSettings()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(
            Path.Combine(temporaryDirectory.RootPath, "AppData"));
        var logger = new RecordingLogger();
        var service = new JsonSettingsService(
            paths,
            logger,
            TimeProvider.System);
        var expected = AppSettings.CreateDefault() with
        {
            PageSize = 250,
            TempRetentionDays = 14,
            WatchRefreshIntervalMilliseconds = 1_000,
            MemoryEditor = new MemoryEditorSettings
            {
                Enabled = true,
                EnabledAt = new DateTimeOffset(
                    2026,
                    7,
                    29,
                    12,
                    0,
                    0,
                    TimeSpan.Zero),
                RequireConfirmation = true,
                VerifyAfterWrite = true,
                AllowManualAddress = false,
            },
        };

        var saveResult = await service.SaveAsync(expected);
        var loadResult = await service.LoadAsync();

        Assert.IsTrue(saveResult.IsSuccess);
        Assert.IsTrue(loadResult.IsSuccess);
        Assert.AreEqual(expected, loadResult.Value);
    }

    [TestMethod]
    public async Task CorruptSettingsAreIsolatedAndDefaultsAreRestored()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(
            Path.Combine(temporaryDirectory.RootPath, "AppData"));
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{ not-json");

        var logger = new RecordingLogger();
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 28, 12, 30, 0, TimeSpan.Zero));
        var service = new JsonSettingsService(paths, logger, timeProvider);

        var result = await service.LoadAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AppSettings.CreateDefault(), result.Value);
        Assert.IsTrue(File.Exists(paths.SettingsFilePath));
        Assert.AreEqual(
            1,
            Directory.GetFiles(
                paths.ConfigDirectory,
                "settings.json.corrupt.*").Length);

        var warning = logger.Entries.Single(entry =>
            entry.Level == AppLogLevel.Warning);
        Assert.IsInstanceOfType<JsonException>(warning.Exception);
        Assert.IsTrue(
            warning.Message.Contains(
                "Default settings",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WellFormedButInvalidSettingsAreAlsoRecovered()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(
            Path.Combine(temporaryDirectory.RootPath, "AppData"));
        paths.EnsureDirectories();

        var invalid = AppSettings.CreateDefault() with { PageSize = 0 };
        await File.WriteAllTextAsync(
            paths.SettingsFilePath,
            JsonSerializer.Serialize(invalid));

        var logger = new RecordingLogger();
        var service = new JsonSettingsService(
            paths,
            logger,
            TimeProvider.System);

        var result = await service.LoadAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AppSettings.CreateDefault(), result.Value);
        Assert.IsInstanceOfType<InvalidDataException>(
            logger.Entries.Single(entry =>
                entry.Level == AppLogLevel.Warning).Exception);
    }

    [TestMethod]
    public async Task InvalidSettingsCannotBeSaved()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(
            Path.Combine(temporaryDirectory.RootPath, "AppData"));
        var service = new JsonSettingsService(
            paths,
            new RecordingLogger(),
            TimeProvider.System);
        var invalid = AppSettings.CreateDefault() with
        {
            MemoryBudgetBytes = 0,
        };

        var result = await service.SaveAsync(invalid);

        Assert.IsTrue(result.IsFailure);
        Assert.IsFalse(File.Exists(paths.SettingsFilePath));
    }
}

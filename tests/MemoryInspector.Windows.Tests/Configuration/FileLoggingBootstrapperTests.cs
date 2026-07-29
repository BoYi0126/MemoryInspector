using MemoryInspector.Application.Logging;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Logging;

namespace MemoryInspector.Windows.Tests.Configuration;

[TestClass]
public sealed class FileLoggingBootstrapperTests
{
    [TestMethod]
    public void InitializeCreatesTheLogDirectoryAndFirstLogFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(
            Path.Combine(temporaryDirectory.RootPath, "AppData"));
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero));
        var bootstrapper = new FileLoggingBootstrapper(paths, timeProvider);

        var result = bootstrapper.Initialize();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(Directory.Exists(paths.LogsDirectory));
        Assert.IsTrue(
            File.Exists(
                Path.Combine(
                    paths.LogsDirectory,
                    "MemoryInspector-20260728.log")));
    }

    [TestMethod]
    public void LoggerRotatesWhenTheLocalDateChanges()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(
            Path.Combine(temporaryDirectory.RootPath, "AppData"));
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 28, 23, 59, 59, TimeSpan.Zero));
        var result = new FileLoggingBootstrapper(paths, timeProvider).Initialize();
        Assert.IsTrue(result.IsSuccess);

        timeProvider.SetUtcNow(
            new DateTimeOffset(2026, 7, 29, 0, 0, 1, TimeSpan.Zero));
        var logResult = result.Value.Log(
            AppLogLevel.Information,
            "A new day has started.");

        Assert.IsTrue(logResult.IsSuccess);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "MemoryInspector-20260728.log",
                "MemoryInspector-20260729.log",
            },
            Directory
                .GetFiles(paths.LogsDirectory, "*.log")
                .Select(Path.GetFileName)
                .ToArray());
    }
}

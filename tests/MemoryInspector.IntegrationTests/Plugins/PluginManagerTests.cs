using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryInspector.Plugin;
using MemoryInspector.Plugin.Runtime;
using MemoryInspector.SamplePlugin;

namespace MemoryInspector.IntegrationTests.Plugins;

[TestClass]
public sealed class PluginManagerTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter(
                    JsonNamingPolicy.CamelCase),
            },
        };

    [TestMethod]
    public async Task LoadsSamplePluginAndExecutesUiContribution()
    {
        using var fixture = new Fixture();
        await fixture.InstallSampleAsync();
        await using var manager = fixture.CreateManager();

        var initialized = await manager.InitializeAsync();
        var contribution = manager
            .GetUiContributions()
            .Single();
        var execution = await contribution.ExecuteAsync();

        Assert.IsTrue(initialized.IsSuccess);
        Assert.AreEqual(1, initialized.Value.LoadedCount);
        Assert.AreEqual(
            PluginLoadState.Loaded,
            initialized.Value.Plugins.Single().State);
        Assert.IsTrue(execution.IsSuccess);
        Assert.AreEqual(
            "Sample analyzer completed.",
            execution.Value.Summary);
        Assert.IsTrue(Directory
            .EnumerateFiles(
                fixture.LogsDirectory,
                "*.log",
                SearchOption.AllDirectories)
            .Any());
    }

    [TestMethod]
    public async Task DisabledPluginIsNotLoadedAfterRestart()
    {
        using var fixture = new Fixture();
        await fixture.InstallSampleAsync();

        await using (var manager = fixture.CreateManager())
        {
            _ = await manager.InitializeAsync();
            var disabled = await manager.DisableAsync(
                "memoryinspector.sample");

            Assert.IsTrue(disabled.IsSuccess);
            Assert.AreEqual(1, disabled.Value.DisabledCount);
            Assert.IsFalse(
                disabled.Value.Plugins.Single().IsLoaded);
            Assert.AreEqual(
                0,
                manager.GetUiContributions().Count);
        }

        await using var restarted = fixture.CreateManager();
        var initialized = await restarted.InitializeAsync();

        Assert.IsTrue(initialized.IsSuccess);
        Assert.AreEqual(0, initialized.Value.LoadedCount);
        Assert.AreEqual(1, initialized.Value.DisabledCount);
        Assert.IsFalse(
            initialized.Value.Plugins.Single().IsLoaded);
    }

    [TestMethod]
    public async Task BrokenPluginDoesNotPreventSampleLoading()
    {
        using var fixture = new Fixture();
        await fixture.InstallSampleAsync();
        await fixture.InstallManifestAsync(
            "broken",
            CreateManifest(
                "memoryinspector.broken",
                entryAssembly: "missing.dll"));
        await using var manager = fixture.CreateManager();

        var initialized = await manager.InitializeAsync();

        Assert.IsTrue(initialized.IsSuccess);
        Assert.AreEqual(1, initialized.Value.LoadedCount);
        Assert.AreEqual(1, initialized.Value.FailedCount);
        Assert.IsTrue(initialized.Value.Plugins.Any(plugin =>
            plugin.Id == "memoryinspector.sample" &&
            plugin.State == PluginLoadState.Loaded));
        Assert.IsTrue(initialized.Value.Plugins.Any(plugin =>
            plugin.Id == "memoryinspector.broken" &&
            plugin.State == PluginLoadState.Failed));
    }

    [TestMethod]
    public async Task IncompatiblePluginIsReportedWithoutAssemblyLoad()
    {
        using var fixture = new Fixture();
        await fixture.InstallManifestAsync(
            "future",
            CreateManifest(
                "memoryinspector.future",
                apiVersion: "2.0.0"));
        await using var manager = fixture.CreateManager();

        var initialized = await manager.InitializeAsync();

        Assert.IsTrue(initialized.IsSuccess);
        Assert.AreEqual(1, initialized.Value.IncompatibleCount);
        Assert.AreEqual(0, initialized.Value.LoadedCount);
        Assert.IsFalse(
            initialized.Value.Plugins.Single().IsLoaded);
    }

    private static PluginManifest CreateManifest(
        string id,
        string entryAssembly =
            "MemoryInspector.SamplePlugin.dll",
        string apiVersion = "1.0.0")
    {
        return new PluginManifest
        {
            Id = id,
            Name = id,
            Version = "1.0.0",
            ApiVersion = apiVersion,
            EntryAssembly = entryAssembly,
            EntryType =
                typeof(SamplePluginModule).FullName!,
            Capabilities = [PluginKind.Analyzer],
        };
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "MemoryInspector.PluginTests",
            Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            PluginsDirectory = Path.Combine(_root, "Plugins");
            LogsDirectory = Path.Combine(_root, "Logs");
            Directory.CreateDirectory(PluginsDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        public string PluginsDirectory { get; }

        public string LogsDirectory { get; }

        public PluginManager CreateManager()
        {
            return new PluginManager(
                PluginsDirectory,
                LogsDirectory);
        }

        public async Task InstallSampleAsync()
        {
            var directory = Path.Combine(
                PluginsDirectory,
                "sample");
            Directory.CreateDirectory(directory);
            var assemblyPath =
                typeof(SamplePluginModule).Assembly.Location;
            File.Copy(
                assemblyPath,
                Path.Combine(
                    directory,
                    Path.GetFileName(assemblyPath)),
                overwrite: true);
            await InstallManifestAsync(
                "sample",
                CreateManifest("memoryinspector.sample"));
        }

        public async Task InstallManifestAsync(
            string directoryName,
            PluginManifest manifest)
        {
            var directory = Path.Combine(
                PluginsDirectory,
                directoryName);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "plugin.json"),
                JsonSerializer.Serialize(
                    manifest,
                    JsonOptions));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}

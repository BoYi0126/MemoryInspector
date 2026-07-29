using MemoryInspector.Windows.Configuration;

namespace MemoryInspector.Windows.Tests.Configuration;

[TestClass]
public sealed class AppPathServiceTests
{
    [TestMethod]
    public void EnsureDirectoriesCreatesTheCompleteApplicationLayout()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = Path.Combine(temporaryDirectory.RootPath, "ApplicationData");
        var service = new AppPathService(root);

        var result = service.EnsureDirectories();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(Directory.Exists(service.RootDirectory));
        Assert.IsTrue(Directory.Exists(service.ConfigDirectory));
        Assert.IsTrue(Directory.Exists(service.TempDirectory));
        Assert.IsTrue(Directory.Exists(service.SessionsDirectory));
        Assert.IsTrue(Directory.Exists(service.SavedAddressesDirectory));
        Assert.IsTrue(Directory.Exists(service.PluginsDirectory));
        Assert.IsTrue(Directory.Exists(service.LogsDirectory));
        Assert.AreEqual(
            Path.Combine(service.ConfigDirectory, "settings.json"),
            service.SettingsFilePath);
    }

    [TestMethod]
    public void DefaultRootUsesTheMemoryInspectorProductName()
    {
        var service = new AppPathService();

        Assert.AreEqual(
            "MemoryInspector",
            Path.GetFileName(service.RootDirectory));
    }
}

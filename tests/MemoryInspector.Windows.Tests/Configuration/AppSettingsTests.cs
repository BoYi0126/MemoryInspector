using MemoryInspector.Application.Configuration;

namespace MemoryInspector.Windows.Tests.Configuration;

[TestClass]
public sealed class AppSettingsTests
{
    [TestMethod]
    public void DefaultsContainTheCurrentSchemaAndOperationalValues()
    {
        var settings = AppSettings.CreateDefault();

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.AreEqual(512L * 1024 * 1024, settings.MemoryBudgetBytes);
        Assert.AreEqual(3, settings.CachedNodeCount);
        Assert.AreEqual(1_000, settings.PageSize);
        Assert.AreEqual(1_000_000L, settings.SnapshotThreshold);
        Assert.AreEqual(7, settings.TempRetentionDays);
        Assert.AreEqual(2_000, settings.ProcessRefreshIntervalMilliseconds);
        Assert.AreEqual(500, settings.WatchRefreshIntervalMilliseconds);
        Assert.AreEqual(0.0001d, settings.DefaultNumericTolerance);
        Assert.IsTrue(settings.Validate().IsSuccess);
    }

    [TestMethod]
    public void ValidationRejectsUnsupportedSchema()
    {
        var settings = AppSettings.CreateDefault() with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion + 1,
        };

        var result = settings.Validate();

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(
            result.Error.Message.Contains(
                "schema version",
                StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ValidationRejectsNonFiniteTolerance()
    {
        var settings = AppSettings.CreateDefault() with
        {
            DefaultNumericTolerance = double.NaN,
        };

        Assert.IsTrue(settings.Validate().IsFailure);
    }
}

using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Memory.Editing;

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
        Assert.AreEqual(100_000L, settings.MemoryOnlyThreshold);
        Assert.AreEqual(1_000_000L, settings.SnapshotThreshold);
        Assert.AreEqual(7, settings.TempRetentionDays);
        Assert.AreEqual(2_000, settings.ProcessRefreshIntervalMilliseconds);
        Assert.AreEqual(500, settings.WatchRefreshIntervalMilliseconds);
        Assert.AreEqual(0.0001d, settings.DefaultNumericTolerance);
        Assert.IsFalse(settings.MemoryEditor.Enabled);
        Assert.IsTrue(settings.MemoryEditor.RequireConfirmation);
        Assert.IsTrue(settings.MemoryEditor.VerifyAfterWrite);
        Assert.IsFalse(settings.MemoryEditor.AllowManualAddress);
        Assert.IsNull(settings.MemoryEditor.EnabledAt);
        Assert.IsTrue(settings.Validate().IsSuccess);
    }

    [TestMethod]
    public void ValidationRejectsInconsistentMemoryEditorEnablement()
    {
        var settings = AppSettings.CreateDefault() with
        {
            MemoryEditor = new MemoryEditorSettings
            {
                Enabled = true,
                EnabledAt = null,
            },
        };

        Assert.IsTrue(settings.Validate().IsFailure);
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

    [TestMethod]
    public void ValidationRejectsReversedCacheThresholds()
    {
        var settings = AppSettings.CreateDefault() with
        {
            MemoryOnlyThreshold = 2_000_000,
            SnapshotThreshold = 1_000_000,
        };

        Assert.IsTrue(settings.Validate().IsFailure);
    }

    [DataRow(49)]
    [DataRow(60_001)]
    [TestMethod]
    public void ValidationRejectsWatchIntervalOutsideSupportedRange(
        int intervalMilliseconds)
    {
        var settings = AppSettings.CreateDefault() with
        {
            WatchRefreshIntervalMilliseconds = intervalMilliseconds,
        };

        Assert.IsTrue(settings.Validate().IsFailure);
    }
}

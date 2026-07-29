using MemoryInspector.Common;

namespace MemoryInspector.Core.Tests.Common;

[TestClass]
public sealed class OperationProgressTests
{
    [TestMethod]
    public void DeterminateProgressCalculatesPercentage()
    {
        var progress = new OperationProgress(25, 100, "Scanning");

        Assert.IsFalse(progress.IsIndeterminate);
        Assert.AreEqual(25d, progress.Percentage);
        Assert.AreEqual("Scanning", progress.Stage);
    }

    [TestMethod]
    public void IndeterminateProgressHasNoPercentage()
    {
        var progress = new OperationProgress(5);

        Assert.IsTrue(progress.IsIndeterminate);
        Assert.IsNull(progress.Percentage);
    }

    [TestMethod]
    public void EmptyCompletedOperationIsOneHundredPercent()
    {
        var progress = new OperationProgress(0, 0);

        Assert.AreEqual(100d, progress.Percentage);
    }

    [TestMethod]
    public void CompletedCannotExceedTotal()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => _ = new OperationProgress(2, 1));
    }
}

using MemoryInspector.Common;
using MemoryInspector.Windows.Scanning.Snapshots;

namespace MemoryInspector.Windows.Tests.Scanning.Snapshots;

[TestClass]
public sealed class SnapshotStorageErrorClassifierTests
{
    [TestMethod]
    [DataRow(39)]
    [DataRow(112)]
    public void DiskFullNativeErrorsAreResourceExhausted(
        int nativeErrorCode)
    {
        var exception = new NativeIOException(nativeErrorCode);

        Assert.IsTrue(
            SnapshotStorageErrorClassifier
                .IsDiskSpaceExhausted(exception));
        Assert.AreEqual(
            ErrorCode.ResourceExhausted,
            SnapshotStorageErrorClassifier.Classify(exception));
    }

    [TestMethod]
    public void OtherIoErrorsRemainIoFailures()
    {
        var exception = new NativeIOException(5);

        Assert.IsFalse(
            SnapshotStorageErrorClassifier
                .IsDiskSpaceExhausted(exception));
        Assert.AreEqual(
            ErrorCode.Io,
            SnapshotStorageErrorClassifier.Classify(exception));
    }

    private sealed class NativeIOException :
        IOException
    {
        public NativeIOException(int nativeErrorCode)
        {
            HResult = unchecked(
                (int)(0x80070000U |
                      (uint)nativeErrorCode));
        }
    }
}

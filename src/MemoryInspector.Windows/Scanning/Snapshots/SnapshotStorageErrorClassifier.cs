using MemoryInspector.Common;

namespace MemoryInspector.Windows.Scanning.Snapshots;

internal static class SnapshotStorageErrorClassifier
{
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorDiskFull = 112;

    public static ErrorCode Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            IOException ioException
                when IsDiskSpaceExhausted(ioException) =>
                ErrorCode.ResourceExhausted,
            IOException or
            UnauthorizedAccessException or
            NotSupportedException =>
                ErrorCode.Io,
            OverflowException or
            OutOfMemoryException =>
                ErrorCode.ResourceExhausted,
            _ => ErrorCode.Unexpected,
        };
    }

    internal static bool IsDiskSpaceExhausted(
        IOException exception)
    {
        var nativeCode = exception.HResult & 0xFFFF;
        return nativeCode is ErrorDiskFull or ErrorHandleDiskFull;
    }
}

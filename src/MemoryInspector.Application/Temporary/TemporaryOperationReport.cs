namespace MemoryInspector.Application.Temporary;

public sealed record TemporaryOperationReport(
    int DeletedSessionCount = 0,
    int DeletedSnapshotCount = 0,
    int DeletedFileCount = 0,
    long ReclaimedBytes = 0,
    int RecoveredFileCount = 0,
    int DiscardedIncompleteFileCount = 0,
    int RetainedPinnedSessionCount = 0,
    int CompactedSessionCount = 0)
{
    public TemporaryOperationReport Add(
        TemporaryOperationReport other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new TemporaryOperationReport(
            DeletedSessionCount + other.DeletedSessionCount,
            DeletedSnapshotCount + other.DeletedSnapshotCount,
            DeletedFileCount + other.DeletedFileCount,
            ReclaimedBytes + other.ReclaimedBytes,
            RecoveredFileCount + other.RecoveredFileCount,
            DiscardedIncompleteFileCount +
                other.DiscardedIncompleteFileCount,
            RetainedPinnedSessionCount +
                other.RetainedPinnedSessionCount,
            CompactedSessionCount + other.CompactedSessionCount);
    }
}

namespace MemoryInspector.Application.Scanning.Snapshots;

public sealed record SnapshotRecoveryResult
{
    public SnapshotRecoveryResult(
        int recoveredFileCount,
        int discardedFileCount)
    {
        if (recoveredFileCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recoveredFileCount));
        }

        if (discardedFileCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(discardedFileCount));
        }

        RecoveredFileCount = recoveredFileCount;
        DiscardedFileCount = discardedFileCount;
    }

    public int RecoveredFileCount { get; }

    public int DiscardedFileCount { get; }
}

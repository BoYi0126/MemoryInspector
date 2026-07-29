namespace MemoryInspector.Application.Scanning.Snapshots.Comparison;

public sealed record SnapshotComparisonSummary
{
    public SnapshotComparisonSummary(
        SnapshotDescriptor left,
        SnapshotDescriptor right,
        long addedCount,
        long removedCount,
        long changedCount,
        long unchangedCount)
    {
        Left = left ??
            throw new ArgumentNullException(nameof(left));
        Right = right ??
            throw new ArgumentNullException(nameof(right));

        if (addedCount < 0 ||
            removedCount < 0 ||
            changedCount < 0 ||
            unchangedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(addedCount));
        }

        AddedCount = addedCount;
        RemovedCount = removedCount;
        ChangedCount = changedCount;
        UnchangedCount = unchangedCount;
    }

    public SnapshotDescriptor Left { get; }

    public SnapshotDescriptor Right { get; }

    public long AddedCount { get; }

    public long RemovedCount { get; }

    public long ChangedCount { get; }

    public long UnchangedCount { get; }

    public long TotalDifferenceCount =>
        AddedCount + RemovedCount + ChangedCount;

    public long TotalComparedAddressCount =>
        TotalDifferenceCount + UnchangedCount;

    public long CountDifference =>
        Right.RecordCount - Left.RecordCount;

    public long StorageSizeDifference =>
        Right.PayloadLength - Left.PayloadLength;
}

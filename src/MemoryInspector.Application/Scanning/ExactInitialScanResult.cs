using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning;

public sealed record ExactInitialScanResult(
    SnapshotDescriptor Snapshot,
    long ScannedBytes,
    int ScannedRegionCount,
    int SkippedRegionCount,
    bool IsResultLimitReached,
    IReadOnlyList<Error> Warnings,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public long CandidateCount => Snapshot.RecordCount;

    public bool IsPartial =>
        IsResultLimitReached || Warnings.Count > 0;
}

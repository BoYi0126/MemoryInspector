using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Snapshots.Comparison;

public sealed record SnapshotComparisonPage(
    SnapshotComparisonSummary Summary,
    PagedResult<SnapshotDifference> Differences)
{
    public SnapshotComparisonSummary Summary { get; } =
        Summary ?? throw new ArgumentNullException(nameof(Summary));

    public PagedResult<SnapshotDifference> Differences { get; } =
        Differences ?? throw new ArgumentNullException(
            nameof(Differences));
}

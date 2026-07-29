using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Snapshots.Comparison;

public interface ISnapshotCompareService
{
    Task<Result<SnapshotComparisonPage>> CompareAsync(
        SnapshotDescriptor left,
        SnapshotDescriptor right,
        long pageNumber = 1,
        int pageSize = SnapshotCompareService.DefaultDifferencePageSize,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<SnapshotComparisonSummary>> VisitAsync(
        SnapshotDescriptor left,
        SnapshotDescriptor right,
        Func<
            SnapshotDifference,
            CancellationToken,
            ValueTask> visitor,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

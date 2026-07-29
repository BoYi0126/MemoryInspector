using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Snapshots.Comparison;

public interface ISnapshotComparisonExportService
{
    Task<Result<SnapshotComparisonSummary>> ExportCsvAsync(
        string path,
        SnapshotDescriptor left,
        SnapshotDescriptor right,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

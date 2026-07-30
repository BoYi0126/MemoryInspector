using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning;

public interface IExactInitialSnapshotService
{
    Task<Result<ExactInitialScanResult>> CreateSnapshotAsync(
        ExactInitialScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

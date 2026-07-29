using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public interface IUnknownInitialScanService
{
    Task<Result<UnknownInitialScanEstimate>> EstimateAsync(
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        CancellationToken cancellationToken = default);

    Task<Result<UnknownInitialScanResult>> CreateSnapshotAsync(
        UnknownInitialScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

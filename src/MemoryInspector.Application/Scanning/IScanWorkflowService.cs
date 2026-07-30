using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public interface IScanWorkflowService
{
    FilterPipelineState? CurrentState { get; }

    Task<Result<UnknownInitialScanEstimate>> EstimateUnknownAsync(
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        CancellationToken cancellationToken = default);

    Task<Result<ScanWorkflowStartResult>> StartExactAsync(
        ScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<ScanWorkflowStartResult>> StartUnknownAsync(
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<PendingFilterResult>> RunNextAsync(
        ScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> KeepAsync(
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> DiscardAsync(
        CancellationToken cancellationToken = default);
}

using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public interface IFilterPipelineService
{
    FilterPipelineState? CurrentState { get; }

    Task<Result<FilterPipelineState>> StartAsync(
        SnapshotDescriptor initialSnapshot,
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> LoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<Result<PendingFilterResult>> RunNextScanAsync(
        ScanRequest filter,
        int pageSize = NextScanRequest.DefaultPageSize,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<PendingFilterResult>> RunDurationFilterAsync(
        ScanRequest filter,
        TimeSpan duration,
        DurationFilterObservationMode observationMode,
        TimeSpan? sampleInterval = null,
        int pageSize = DurationFilterRequest.DefaultPageSize,
        DurationFilterExecutionControl? executionControl = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> KeepResultAsync(
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> DiscardResultAsync(
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> UndoAsync(
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> RedoAsync(
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> RenameRoundAsync(
        Guid roundId,
        string name,
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> DeletePendingRoundAsync(
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> BranchFromAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> SetActiveNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> RenameNodeAsync(
        Guid nodeId,
        string name,
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> SetNodePinnedAsync(
        Guid nodeId,
        bool isPinned,
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> DeleteBranchAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default);

    Result CloseSession(Guid sessionId);

    Result<ScanTreeNodeComparison> CompareNodes(
        Guid leftNodeId,
        Guid rightNodeId);

    Result<IReadOnlyList<ScanTreeNode>> GetChildNodes(
        Guid nodeId);

    Result<IReadOnlyList<ScanTreeNode>> GetPathToRoot(
        Guid nodeId);
}

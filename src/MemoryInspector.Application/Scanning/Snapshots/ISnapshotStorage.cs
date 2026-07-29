using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Snapshots;

public interface ISnapshotStorage
{
    bool IsOperationInProgress => false;

    Task<Result<SnapshotDescriptor>> WriteAsync(
        SnapshotWriteRequest request,
        IAsyncEnumerable<SnapshotRecord> records,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<SnapshotDescriptor>> OpenAsync(
        Guid sessionId,
        int nodeId,
        CancellationToken cancellationToken = default);

    Task<Result<SnapshotDescriptor>> OptimizeAsync(
        SnapshotDescriptor parentSnapshot,
        SnapshotDescriptor fullSnapshot,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SnapshotRecord>>> ReadPageAsync(
        SnapshotDescriptor snapshot,
        long pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        Guid sessionId,
        int nodeId,
        CancellationToken cancellationToken = default);

    Task<Result<SnapshotRecoveryResult>> RecoverIncompleteAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

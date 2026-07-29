using MemoryInspector.Common;

namespace MemoryInspector.Application.Temporary;

public interface ITemporaryManagerService
{
    string TempDirectory { get; }

    Task<Result<TemporaryStorageSnapshot>> InspectAsync(
        CancellationToken cancellationToken = default);

    Task<Result<TemporaryOperationReport>> RunAutomaticCleanupAsync(
        CancellationToken cancellationToken = default);

    Task<Result<TemporaryOperationReport>> DeleteCurrentNodeAsync(
        CancellationToken cancellationToken = default);

    Task<Result<TemporaryOperationReport>> DeleteBranchAsync(
        Guid roundId,
        CancellationToken cancellationToken = default);

    Task<Result<TemporaryOperationReport>> DeleteSessionAsync(
        Guid sessionId,
        bool includePinned = false,
        CancellationToken cancellationToken = default);

    Task<Result<TemporaryOperationReport>> DeleteAllAsync(
        bool includePinned = false,
        CancellationToken cancellationToken = default);

    Task<Result<TemporaryOperationReport>> CompactSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Result OpenTempFolder();
}

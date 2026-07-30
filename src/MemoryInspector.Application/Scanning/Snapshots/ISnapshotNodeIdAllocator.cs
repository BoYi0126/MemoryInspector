using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Snapshots;

public interface ISnapshotNodeIdAllocator
{
    Task<Result<int>> ReserveAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

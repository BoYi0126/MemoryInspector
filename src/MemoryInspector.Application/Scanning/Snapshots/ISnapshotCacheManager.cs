using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Snapshots;

public interface ISnapshotCacheManager
{
    SnapshotCachePolicy CurrentPolicy { get; }

    IReadOnlyList<SnapshotCacheEntryInfo> GetCachedNodes();

    Task<Result<SnapshotCacheUsage>> GetUsageAsync(
        Guid? sessionId = null,
        CancellationToken cancellationToken = default);

    Task<Result<SnapshotCacheUsage>> UpdatePolicyAsync(
        SnapshotCachePolicy policy,
        bool persist = true,
        CancellationToken cancellationToken = default);

    Result Clear(Guid? sessionId = null);
}

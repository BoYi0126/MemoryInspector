namespace MemoryInspector.Application.Scanning.Snapshots;

public sealed record SnapshotCacheUsage(
    long MemoryBytes,
    long MemoryBudgetBytes,
    int CachedNodeCount,
    int MaximumCachedNodes,
    long CachedRecordCount,
    long DiskBytes,
    long CacheHits,
    long CacheMisses,
    long EvictionCount)
{
    public long AvailableMemoryBytes =>
        Math.Max(0, MemoryBudgetBytes - MemoryBytes);

    public double MemoryUtilization =>
        MemoryBudgetBytes == 0
            ? 0
            : (double)MemoryBytes / MemoryBudgetBytes;
}

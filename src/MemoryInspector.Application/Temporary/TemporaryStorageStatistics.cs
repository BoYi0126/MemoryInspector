namespace MemoryInspector.Application.Temporary;

public sealed record TemporaryStorageStatistics(
    int SessionCount,
    int FileCount,
    int SnapshotCount,
    int IncompleteFileCount,
    int PinnedNodeCount,
    long TotalBytes,
    long CachedBytes);

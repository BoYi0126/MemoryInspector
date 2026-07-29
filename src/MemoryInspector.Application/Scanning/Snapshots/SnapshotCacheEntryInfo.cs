namespace MemoryInspector.Application.Scanning.Snapshots;

public sealed record SnapshotCacheEntryInfo(
    Guid SessionId,
    int NodeId,
    long RecordCount,
    long MemoryBytes,
    DateTimeOffset LastAccessedAt);

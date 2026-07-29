namespace MemoryInspector.Application.Temporary;

public sealed record TemporarySessionInfo(
    Guid SessionId,
    long TotalBytes,
    int FileCount,
    int SnapshotCount,
    int IncompleteFileCount,
    int PinnedNodeCount,
    DateTimeOffset LastModifiedAt,
    bool HasReadableHistory,
    bool IsCurrent);

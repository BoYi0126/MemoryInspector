namespace MemoryInspector.Application.Temporary;

public sealed record TemporaryStorageSnapshot(
    IReadOnlyList<TemporarySessionInfo> Sessions,
    TemporaryStorageStatistics Statistics);

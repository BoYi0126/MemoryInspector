namespace MemoryInspector.Application.Watch;

public sealed record WatchRefreshResult(
    int AttemptedCount,
    int AvailableCount,
    int UnreadableCount,
    DateTimeOffset CompletedAt)
{
    public bool IsPartial => UnreadableCount > 0;
}

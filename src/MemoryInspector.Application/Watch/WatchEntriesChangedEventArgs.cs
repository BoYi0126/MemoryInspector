namespace MemoryInspector.Application.Watch;

public sealed class WatchEntriesChangedEventArgs(
    IEnumerable<WatchEntry> entries,
    bool isPaused,
    bool canRefresh) : EventArgs
{
    public IReadOnlyList<WatchEntry> Entries { get; } =
        Array.AsReadOnly(
            entries?.ToArray() ??
            throw new ArgumentNullException(nameof(entries)));

    public bool IsPaused { get; } = isPaused;

    public bool CanRefresh { get; } = canRefresh;
}

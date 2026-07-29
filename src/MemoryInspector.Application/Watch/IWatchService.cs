using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Watch;

public interface IWatchService
{
    IReadOnlyList<WatchEntry> Entries { get; }

    bool IsPaused { get; }

    bool CanRefresh { get; }

    event EventHandler<WatchEntriesChangedEventArgs>?
        EntriesChanged;

    Result<WatchEntry> Add(
        ulong address,
        ScanValueType valueType);

    Result Remove(Guid key);

    Result<WatchEntry> ChangeType(
        Guid key,
        ScanValueType valueType);

    Result SetPaused(bool isPaused);

    Task<Result<WatchRefreshResult>> RefreshAsync(
        CancellationToken cancellationToken = default);
}

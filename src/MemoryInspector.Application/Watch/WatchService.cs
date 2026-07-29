using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Watch;

public sealed class WatchService :
    IWatchService,
    IDisposable
{
    private readonly object _sync = new();
    private readonly IMemoryReaderService _memoryReaderService;
    private readonly IMonitoringSessionService
        _monitoringSessionService;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly List<WatchEntry> _entries = [];
    private Guid? _boundSessionId;
    private bool _isPaused;
    private bool _disposed;

    public WatchService(
        IMemoryReaderService memoryReaderService,
        IMonitoringSessionService monitoringSessionService,
        TimeProvider timeProvider)
    {
        _memoryReaderService =
            Guard.NotNull(memoryReaderService);
        _monitoringSessionService =
            Guard.NotNull(monitoringSessionService);
        _timeProvider = Guard.NotNull(timeProvider);
        monitoringSessionService.SessionChanged +=
            OnSessionChanged;
    }

    public IReadOnlyList<WatchEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return Array.AsReadOnly(
                    _entries.ToArray());
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _isPaused;
            }
        }
    }

    public bool CanRefresh
    {
        get
        {
            lock (_sync)
            {
                return CanRefreshCore();
            }
        }
    }

    public event EventHandler<WatchEntriesChangedEventArgs>?
        EntriesChanged;

    public Result<WatchEntry> Add(
        ulong address,
        ScanValueType valueType)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ScanValueTypeInfo.GetSize(valueType);
        WatchEntry entry;

        lock (_sync)
        {
            var session = GetConnectedSession();

            if (session is null)
            {
                return InvalidState<WatchEntry>(
                    "A connected monitoring session is required.");
            }

            if (_boundSessionId.HasValue &&
                _boundSessionId.Value != session.SessionId)
            {
                return InvalidState<WatchEntry>(
                    "Remove watch entries from the previous " +
                    "session before adding a new target.");
            }

            var existing = _entries.FirstOrDefault(candidate =>
                candidate.Address == address &&
                candidate.ValueType == valueType);

            if (existing is not null)
            {
                return Result<WatchEntry>.Success(existing);
            }

            _boundSessionId ??= session.SessionId;
            entry = new WatchEntry(
                Guid.NewGuid(),
                address,
                valueType,
                status: _isPaused
                    ? WatchReadStatus.Paused
                    : WatchReadStatus.Pending);
            _entries.Add(entry);
        }

        PublishChanged();
        return Result<WatchEntry>.Success(entry);
    }

    public Result Remove(Guid key)
    {
        if (key == Guid.Empty)
        {
            return Validation("Watch key cannot be empty.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            var index = _entries.FindIndex(entry =>
                entry.Key == key);

            if (index < 0)
            {
                return NotFound();
            }

            _entries.RemoveAt(index);

            if (_entries.Count == 0)
            {
                _boundSessionId = null;
            }
        }

        PublishChanged();
        return Result.Success();
    }

    public Result<WatchEntry> ChangeType(
        Guid key,
        ScanValueType valueType)
    {
        if (key == Guid.Empty)
        {
            return Validation<WatchEntry>(
                "Watch key cannot be empty.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ScanValueTypeInfo.GetSize(valueType);
        WatchEntry changed;

        lock (_sync)
        {
            var index = _entries.FindIndex(entry =>
                entry.Key == key);

            if (index < 0)
            {
                return Result<WatchEntry>.Failure(
                    NotFound().Error);
            }

            changed = _entries[index].ChangeType(valueType);

            if (_isPaused)
            {
                changed = changed.WithStatus(
                    WatchReadStatus.Paused);
            }
            else if (!IsBoundSessionConnected())
            {
                changed = changed.WithStatus(
                    WatchReadStatus.TargetUnavailable,
                    "The monitored process is unavailable.");
            }

            _entries[index] = changed;
        }

        PublishChanged();
        return Result<WatchEntry>.Success(changed);
    }

    public Result SetPaused(bool isPaused)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            if (_isPaused == isPaused)
            {
                return Result.Success();
            }

            if (!isPaused &&
                !IsBoundSessionConnected())
            {
                return InvalidState(
                    "The original monitoring session is unavailable.");
            }

            _isPaused = isPaused;

            for (var index = 0;
                 index < _entries.Count;
                 index++)
            {
                _entries[index] = isPaused
                    ? _entries[index].WithStatus(
                        WatchReadStatus.Paused)
                    : _entries[index].WithStatus(
                        WatchReadStatus.Pending);
            }
        }

        PublishChanged();
        return Result.Success();
    }

    public async Task<Result<WatchRefreshResult>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _refreshGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(exception);
        }

        try
        {
            WatchEntry[] requestedEntries;
            var sessionUnavailable = false;

            lock (_sync)
            {
                if (_isPaused)
                {
                    return InvalidRefreshState(
                        "Watch refresh is paused.");
                }

                if (!IsBoundSessionConnected())
                {
                    if (_entries.Any(entry =>
                        entry.Status !=
                        WatchReadStatus.TargetUnavailable))
                    {
                        MarkAllUnavailableCore(
                            "The monitored process is unavailable.");
                    }

                    sessionUnavailable = true;
                }

                requestedEntries = _entries.ToArray();
            }

            if (sessionUnavailable)
            {
                PublishChanged();
                return InvalidRefreshState(
                    "The original monitoring session is unavailable.");
            }

            if (requestedEntries.Length == 0)
            {
                return Result<WatchRefreshResult>.Success(
                    new WatchRefreshResult(
                        0,
                        0,
                        0,
                        _timeProvider.GetUtcNow()));
            }

            var requests = requestedEntries
                .Select(entry =>
                    new MemoryReadRequest(
                        entry.Address,
                        ScanValueTypeInfo.GetSize(
                            entry.ValueType)))
                .ToArray();
            var readResult =
                await _memoryReaderService.ReadBatchAsync(
                    requests,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var completedAt = _timeProvider.GetUtcNow();

            if (readResult.IsFailure)
            {
                lock (_sync)
                {
                    MarkAllUnavailableCore(
                        readResult.Error.ToDisplayMessage(),
                        completedAt);
                }

                PublishChanged();
                return Result<WatchRefreshResult>.Failure(
                    readResult.Error);
            }

            if (readResult.Value.Items.Count !=
                requestedEntries.Length)
            {
                return Result<WatchRefreshResult>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "Batch memory read returned an " +
                        "unexpected item count."));
            }

            var availableCount = 0;
            var unreadableCount = 0;

            lock (_sync)
            {
                for (var index = 0;
                     index < requestedEntries.Length;
                     index++)
                {
                    var requested = requestedEntries[index];
                    var currentIndex = _entries.FindIndex(entry =>
                        entry.Key == requested.Key &&
                        entry.ValueType == requested.ValueType);

                    if (currentIndex < 0)
                    {
                        continue;
                    }

                    var item = readResult.Value.Items[index];

                    if (item.Result.IsSuccess &&
                        item.Result.Value.IsComplete &&
                        item.Result.Value.Data.Length ==
                        requests[index].Length)
                    {
                        _entries[currentIndex] =
                            _entries[currentIndex]
                                .WithSuccessfulRead(
                                    item.Result.Value.Data,
                                    completedAt);
                        availableCount++;
                    }
                    else
                    {
                        var message = item.Result.IsFailure
                            ? item.Result.Error.ToDisplayMessage()
                            : "A complete value could not be read.";
                        _entries[currentIndex] =
                            _entries[currentIndex].WithFailure(
                                WatchReadStatus.Unreadable,
                                message,
                                completedAt);
                        unreadableCount++;
                    }
                }
            }

            PublishChanged();
            return Result<WatchRefreshResult>.Success(
                new WatchRefreshResult(
                    requestedEntries.Length,
                    availableCount,
                    unreadableCount,
                    completedAt));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(exception);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _monitoringSessionService.SessionChanged -=
            OnSessionChanged;
        _refreshGate.Dispose();
        _disposed = true;
    }

    private void OnSessionChanged(
        object? sender,
        MonitoringSessionChangedEventArgs eventArgs)
    {
        var changed = false;

        lock (_sync)
        {
            if (_entries.Count > 0 &&
                (_boundSessionId != eventArgs.Session.SessionId ||
                 eventArgs.Session.State !=
                 MonitoringSessionState.Connected))
            {
                MarkAllUnavailableCore(
                    eventArgs.Session.StatusMessage ??
                    "The monitored process is unavailable.");
                changed = true;
            }
        }

        if (changed)
        {
            PublishChanged();
        }
    }

    private MonitoringSession? GetConnectedSession()
    {
        var session =
            _monitoringSessionService.CurrentSession;
        return session?.State ==
            MonitoringSessionState.Connected
            ? session
            : null;
    }

    private bool IsBoundSessionConnected()
    {
        var session = GetConnectedSession();
        return _boundSessionId.HasValue &&
               session?.SessionId == _boundSessionId.Value;
    }

    private bool CanRefreshCore()
    {
        return !_isPaused &&
               _entries.Count > 0 &&
               IsBoundSessionConnected();
    }

    private void MarkAllUnavailableCore(
        string message,
        DateTimeOffset? updatedAt = null)
    {
        var timestamp = updatedAt ??
            _timeProvider.GetUtcNow();

        for (var index = 0;
             index < _entries.Count;
             index++)
        {
            _entries[index] = _entries[index].WithFailure(
                WatchReadStatus.TargetUnavailable,
                message,
                timestamp);
        }
    }

    private void PublishChanged()
    {
        WatchEntriesChangedEventArgs eventArgs;

        lock (_sync)
        {
            eventArgs = new WatchEntriesChangedEventArgs(
                _entries,
                _isPaused,
                CanRefreshCore());
        }

        EntriesChanged?.Invoke(this, eventArgs);
    }

    private static Result Validation(string message)
    {
        return Result.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private static Result<T> Validation<T>(string message)
    {
        return Result<T>.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private static Result InvalidState(string message)
    {
        return Result.Failure(
            new Error(ErrorCode.InvalidState, message));
    }

    private static Result<T> InvalidState<T>(string message)
    {
        return Result<T>.Failure(
            new Error(ErrorCode.InvalidState, message));
    }

    private static Result NotFound()
    {
        return Result.Failure(
            new Error(
                ErrorCode.NotFound,
                "The watch entry was not found."));
    }

    private static Result<WatchRefreshResult>
        InvalidRefreshState(string message)
    {
        return Result<WatchRefreshResult>.Failure(
            new Error(ErrorCode.InvalidState, message));
    }

    private static Result<WatchRefreshResult> Cancelled(
        OperationCanceledException exception)
    {
        return Result<WatchRefreshResult>.Failure(
            new Error(
                ErrorCode.Cancelled,
                "Watch refresh was cancelled.",
                exception));
    }
}

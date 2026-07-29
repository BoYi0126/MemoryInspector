using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Windows.Scanning.Snapshots;

public sealed class LruSnapshotStorage :
    ISnapshotStorage,
    ISnapshotCacheManager,
    IDisposable
{
    private readonly object _sync = new();
    private readonly ISnapshotStorage _inner;
    private readonly ISettingsService _settingsService;
    private readonly IAppPathService _pathService;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<SnapshotCacheKey, CacheEntry>
        _entries = [];
    private readonly LinkedList<SnapshotCacheKey> _recency = [];
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _policyGate = new(1, 1);
    private SnapshotCachePolicy _policy = new();
    private bool _policyInitialized;
    private long _memoryBytes;
    private long _cachedRecordCount;
    private long _cacheHits;
    private long _cacheMisses;
    private long _evictionCount;
    private int _activeOperationCount;
    private bool _disposed;

    public LruSnapshotStorage(
        ISnapshotStorage inner,
        ISettingsService settingsService,
        IAppPathService pathService,
        TimeProvider timeProvider)
    {
        _inner = Guard.NotNull(inner);
        _settingsService = Guard.NotNull(settingsService);
        _pathService = Guard.NotNull(pathService);
        _timeProvider = Guard.NotNull(timeProvider);
    }

    public SnapshotCachePolicy CurrentPolicy
    {
        get
        {
            lock (_sync)
            {
                return _policy;
            }
        }
    }

    public bool IsOperationInProgress =>
        Volatile.Read(ref _activeOperationCount) > 0 ||
        _inner.IsOperationInProgress;

    public async Task<Result<SnapshotDescriptor>> WriteAsync(
        SnapshotWriteRequest request,
        IAsyncEnumerable<SnapshotRecord> records,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = BeginOperation();
        await EnsurePolicyLoadedAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = await _inner.WriteAsync(
                request,
                records,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            RemoveEntry(
                new SnapshotCacheKey(
                    result.Value.SessionId,
                    result.Value.NodeId),
                countAsEviction: false);
            await WarmIfMemoryPreferredAsync(
                    result.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task<Result<SnapshotDescriptor>> OpenAsync(
        Guid sessionId,
        int nodeId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = BeginOperation();
        await EnsurePolicyLoadedAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = await _inner.OpenAsync(
                sessionId,
                nodeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await WarmIfMemoryPreferredAsync(
                    result.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task<Result<SnapshotDescriptor>> OptimizeAsync(
        SnapshotDescriptor parentSnapshot,
        SnapshotDescriptor fullSnapshot,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = BeginOperation();
        await EnsurePolicyLoadedAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = await _inner.OptimizeAsync(
                parentSnapshot,
                fullSnapshot,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            RemoveEntry(
                new SnapshotCacheKey(
                    fullSnapshot.SessionId,
                    fullSnapshot.NodeId),
                countAsEviction: false);
            await WarmIfMemoryPreferredAsync(
                    result.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    public async Task<Result<PagedResult<SnapshotRecord>>>
        ReadPageAsync(
            SnapshotDescriptor snapshot,
            long pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
    {
        if (snapshot is null)
        {
            return Validation<PagedResult<SnapshotRecord>>(
                "A snapshot descriptor is required.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = BeginOperation();
        await EnsurePolicyLoadedAsync(cancellationToken)
            .ConfigureAwait(false);

        var key = new SnapshotCacheKey(
            snapshot.SessionId,
            snapshot.NodeId);

        if (TryGetEntry(key, snapshot, out var cached))
        {
            return CreatePage(
                cached,
                pageNumber,
                pageSize);
        }

        Interlocked.Increment(ref _cacheMisses);

        if (!CanAttemptCache(snapshot))
        {
            return await _inner.ReadPageAsync(
                    snapshot,
                    pageNumber,
                    pageSize,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            await _loadGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<PagedResult<SnapshotRecord>>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Snapshot cache loading was cancelled.",
                    exception));
        }

        try
        {
            if (TryGetEntry(key, snapshot, out cached))
            {
                return CreatePage(
                    cached,
                    pageNumber,
                    pageSize);
            }

            var loadResult = await LoadEntryAsync(
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);

            if (loadResult.IsFailure)
            {
                return Result<PagedResult<SnapshotRecord>>.Failure(
                    loadResult.Error);
            }

            if (loadResult.Value is not null)
            {
                AddEntry(loadResult.Value);

                if (TryGetEntry(
                    key,
                    snapshot,
                    out cached))
                {
                    return CreatePage(
                        cached,
                        pageNumber,
                        pageSize);
                }
            }
        }
        finally
        {
            _loadGate.Release();
        }

        return await _inner.ReadPageAsync(
                snapshot,
                pageNumber,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result> DeleteAsync(
        Guid sessionId,
        int nodeId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = BeginOperation();
        var result = await _inner.DeleteAsync(
                sessionId,
                nodeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess ||
            result.Error.Code == ErrorCode.NotFound)
        {
            RemoveEntry(
                new SnapshotCacheKey(sessionId, nodeId),
                countAsEviction: false);
        }

        return result;
    }

    public async Task<Result<SnapshotRecoveryResult>>
        RecoverIncompleteAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = BeginOperation();
        var result = await _inner.RecoverIncompleteAsync(
                sessionId,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _ = Clear(sessionId);
        }

        return result;
    }

    public IReadOnlyList<SnapshotCacheEntryInfo>
        GetCachedNodes()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            return _recency
                .Select(key => _entries[key].ToInfo())
                .ToArray();
        }
    }

    public async Task<Result<SnapshotCacheUsage>> GetUsageAsync(
        Guid? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Validation<SnapshotCacheUsage>(
                "Session ID cannot be empty.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsurePolicyLoadedAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var diskBytes = CalculateDiskBytes(
                sessionId,
                cancellationToken);

            lock (_sync)
            {
                var memoryBytes = sessionId is null
                    ? _memoryBytes
                    : _entries.Values
                        .Where(entry =>
                            entry.Key.SessionId == sessionId)
                        .Sum(entry => entry.MemoryBytes);
                var recordCount = sessionId is null
                    ? _cachedRecordCount
                    : _entries.Values
                        .Where(entry =>
                            entry.Key.SessionId == sessionId)
                        .Sum(entry => entry.RecordCount);
                var nodeCount = sessionId is null
                    ? _entries.Count
                    : _entries.Values.Count(entry =>
                        entry.Key.SessionId == sessionId);

                return Result<SnapshotCacheUsage>.Success(
                    new SnapshotCacheUsage(
                        memoryBytes,
                        _policy.MemoryBudgetBytes,
                        nodeCount,
                        _policy.MaximumCachedNodes,
                        recordCount,
                        diskBytes,
                        Interlocked.Read(ref _cacheHits),
                        Interlocked.Read(ref _cacheMisses),
                        Interlocked.Read(ref _evictionCount)));
            }
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<SnapshotCacheUsage>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Calculating snapshot usage was cancelled.",
                    exception));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return Result<SnapshotCacheUsage>.Failure(
                new Error(
                    ErrorCode.Io,
                    "Snapshot disk usage could not be calculated.",
                    exception));
        }
    }

    public async Task<Result<SnapshotCacheUsage>>
        UpdatePolicyAsync(
            SnapshotCachePolicy policy,
            bool persist = true,
            CancellationToken cancellationToken = default)
    {
        if (policy is null)
        {
            return Validation<SnapshotCacheUsage>(
                "A snapshot cache policy is required.");
        }

        var validation = policy.Validate();

        if (validation.IsFailure)
        {
            return Result<SnapshotCacheUsage>.Failure(
                validation.Error);
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (persist)
        {
            Result<AppSettings> loadResult;

            try
            {
                loadResult = await _settingsService.LoadAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                return CancelledUsage(exception);
            }

            if (loadResult.IsFailure)
            {
                return Result<SnapshotCacheUsage>.Failure(
                    loadResult.Error);
            }

            var settings = loadResult.Value with
            {
                MemoryBudgetBytes = policy.MemoryBudgetBytes,
                CachedNodeCount = policy.MaximumCachedNodes,
                PageSize = policy.PageSize,
                MemoryOnlyThreshold =
                    policy.MemoryOnlyThreshold,
                SnapshotThreshold =
                    policy.DiskBackedThreshold,
            };
            Result saveResult;

            try
            {
                saveResult = await _settingsService.SaveAsync(
                        settings,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                return CancelledUsage(exception);
            }

            if (saveResult.IsFailure)
            {
                return Result<SnapshotCacheUsage>.Failure(
                    saveResult.Error);
            }
        }

        lock (_sync)
        {
            _policy = policy;
            _policyInitialized = true;
            EvictToPolicy();
        }

        return await GetUsageAsync(
                sessionId: null,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    public Result Clear(Guid? sessionId = null)
    {
        if (sessionId == Guid.Empty)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Session ID cannot be empty."));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            var keys = sessionId is null
                ? _entries.Keys.ToArray()
                : _entries.Keys
                    .Where(key => key.SessionId == sessionId)
                    .ToArray();

            foreach (var key in keys)
            {
                RemoveEntryCore(
                    key,
                    countAsEviction: false);
            }
        }

        return Result.Success();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            _entries.Clear();
            _recency.Clear();
            _memoryBytes = 0;
            _cachedRecordCount = 0;
        }

        _loadGate.Dispose();
        _policyGate.Dispose();
        _disposed = true;
    }

    private IDisposable BeginOperation()
    {
        Interlocked.Increment(ref _activeOperationCount);
        return new OperationLease(this);
    }

    private sealed class OperationLease(
        LruSnapshotStorage owner) : IDisposable
    {
        private LruSnapshotStorage? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(
                ref _owner,
                null);

            if (current is not null)
            {
                Interlocked.Decrement(
                    ref current._activeOperationCount);
            }
        }
    }

    private async Task EnsurePolicyLoadedAsync(
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_policyInitialized)
            {
                return;
            }
        }

        try
        {
            await _policyGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                if (_policyInitialized)
                {
                    return;
                }
            }

            Result<AppSettings> loadResult;

            try
            {
                loadResult = await _settingsService.LoadAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            var loadedPolicy = loadResult.IsSuccess
                ? SnapshotCachePolicy.FromSettings(
                    loadResult.Value)
                : new SnapshotCachePolicy();

            if (loadedPolicy.Validate().IsFailure)
            {
                loadedPolicy = new SnapshotCachePolicy();
            }

            lock (_sync)
            {
                _policy = loadedPolicy;
                _policyInitialized = true;
                EvictToPolicy();
            }
        }
        finally
        {
            _policyGate.Release();
        }
    }

    private async Task WarmIfMemoryPreferredAsync(
        SnapshotDescriptor snapshot,
        CancellationToken cancellationToken)
    {
        SnapshotCachePolicy policy;

        lock (_sync)
        {
            policy = _policy;
        }

        if (snapshot.RecordCount >
            policy.MemoryOnlyThreshold ||
            !CanAttemptCache(snapshot))
        {
            return;
        }

        var key = new SnapshotCacheKey(
            snapshot.SessionId,
            snapshot.NodeId);

        if (TryGetEntry(key, snapshot, out _))
        {
            return;
        }

        try
        {
            await _loadGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (TryGetEntry(key, snapshot, out _))
            {
                return;
            }

            var result = await LoadEntryAsync(
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess &&
                result.Value is not null)
            {
                AddEntry(result.Value);
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private bool CanAttemptCache(
        SnapshotDescriptor snapshot)
    {
        SnapshotCachePolicy policy;

        lock (_sync)
        {
            policy = _policy;
        }

        if (snapshot.RecordCount >=
                policy.DiskBackedThreshold ||
            snapshot.RecordCount > Array.MaxLength)
        {
            return false;
        }

        try
        {
            var memoryBytes = snapshot.FullPayloadLength;
            var valueBytes = checked(
                snapshot.RecordCount * snapshot.ValueSize);

            return memoryBytes <= policy.MemoryBudgetBytes &&
                   valueBytes <= Array.MaxLength;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private async Task<Result<CacheEntry?>> LoadEntryAsync(
        SnapshotDescriptor snapshot,
        CancellationToken cancellationToken)
    {
        if (!CanAttemptCache(snapshot))
        {
            return Result<CacheEntry?>.Success(null);
        }

        SnapshotCachePolicy policy;

        lock (_sync)
        {
            policy = _policy;
        }

        try
        {
            var count = checked((int)snapshot.RecordCount);
            var addresses = new ulong[count];
            var values = snapshot.ValueSize == 0
                ? []
                : new byte[checked(count * snapshot.ValueSize)];
            var copied = 0;
            long pageNumber = 1;

            while (copied < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageResult = await _inner.ReadPageAsync(
                        snapshot,
                        pageNumber,
                        policy.PageSize,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (pageResult.IsFailure)
                {
                    return Result<CacheEntry?>.Failure(
                        pageResult.Error);
                }

                if (pageResult.Value.Items.Count == 0)
                {
                    break;
                }

                foreach (var record in pageResult.Value.Items)
                {
                    if (copied >= count ||
                        record.Value.Length !=
                        snapshot.ValueSize)
                    {
                        return InvalidCacheEntry(
                            "Snapshot records changed while " +
                            "the cache was loading.");
                    }

                    addresses[copied] =
                        record.Candidate.Address;

                    if (snapshot.ValueSize > 0)
                    {
                        record.Value.Span.CopyTo(
                            values.AsSpan(
                                checked(
                                    copied *
                                    snapshot.ValueSize),
                                snapshot.ValueSize));
                    }

                    copied++;
                }

                pageNumber++;
            }

            if (copied != count)
            {
                return InvalidCacheEntry(
                    "Snapshot record count changed while " +
                    "the cache was loading.");
            }

            return Result<CacheEntry?>.Success(
                new CacheEntry(
                    new SnapshotCacheKey(
                        snapshot.SessionId,
                        snapshot.NodeId),
                    snapshot,
                    addresses,
                    values,
                    _timeProvider.GetUtcNow()));
        }
        catch (OutOfMemoryException)
        {
            return Result<CacheEntry?>.Success(null);
        }
        catch (OverflowException)
        {
            return Result<CacheEntry?>.Success(null);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<CacheEntry?>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Snapshot cache loading was cancelled.",
                    exception));
        }
    }

    private bool TryGetEntry(
        SnapshotCacheKey key,
        SnapshotDescriptor snapshot,
        out CacheEntry entry)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out entry!) ||
                !entry.Matches(snapshot))
            {
                return false;
            }

            _recency.Remove(entry.RecencyNode);
            _recency.AddFirst(entry.RecencyNode);
            entry.LastAccessedAt =
                _timeProvider.GetUtcNow();
            Interlocked.Increment(ref _cacheHits);
            return true;
        }
    }

    private void AddEntry(CacheEntry entry)
    {
        lock (_sync)
        {
            if (!CanCacheUnderPolicy(entry))
            {
                return;
            }

            RemoveEntryCore(
                entry.Key,
                countAsEviction: false);

            while (_entries.Count >=
                       _policy.MaximumCachedNodes ||
                   entry.MemoryBytes >
                   _policy.MemoryBudgetBytes -
                   _memoryBytes)
            {
                var leastRecent = _recency.Last;

                if (leastRecent is null)
                {
                    return;
                }

                RemoveEntryCore(
                    leastRecent.Value,
                    countAsEviction: true);
            }

            entry.RecencyNode =
                _recency.AddFirst(entry.Key);
            _entries.Add(entry.Key, entry);
            _memoryBytes = checked(
                _memoryBytes + entry.MemoryBytes);
            _cachedRecordCount = checked(
                _cachedRecordCount +
                entry.RecordCount);
        }
    }

    private bool CanCacheUnderPolicy(CacheEntry entry)
    {
        return entry.RecordCount <
                   _policy.DiskBackedThreshold &&
               entry.MemoryBytes <=
                   _policy.MemoryBudgetBytes;
    }

    private void EvictToPolicy()
    {
        var ineligible = _entries.Values
            .Where(entry => !CanCacheUnderPolicy(entry))
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var key in ineligible)
        {
            RemoveEntryCore(
                key,
                countAsEviction: true);
        }

        while (_entries.Count >
                   _policy.MaximumCachedNodes ||
               _memoryBytes >
                   _policy.MemoryBudgetBytes)
        {
            var leastRecent = _recency.Last;

            if (leastRecent is null)
            {
                break;
            }

            RemoveEntryCore(
                leastRecent.Value,
                countAsEviction: true);
        }
    }

    private void RemoveEntry(
        SnapshotCacheKey key,
        bool countAsEviction)
    {
        lock (_sync)
        {
            RemoveEntryCore(key, countAsEviction);
        }
    }

    private void RemoveEntryCore(
        SnapshotCacheKey key,
        bool countAsEviction)
    {
        if (!_entries.Remove(key, out var entry))
        {
            return;
        }

        _recency.Remove(entry.RecencyNode);
        _memoryBytes -= entry.MemoryBytes;
        _cachedRecordCount -= entry.RecordCount;

        if (countAsEviction)
        {
            Interlocked.Increment(ref _evictionCount);
        }
    }

    private static Result<PagedResult<SnapshotRecord>>
        CreatePage(
            CacheEntry entry,
            long pageNumber,
            int pageSize)
    {
        if (pageNumber <= 0)
        {
            return Validation<PagedResult<SnapshotRecord>>(
                "Page number must be greater than zero.");
        }

        if (pageSize <= 0 ||
            pageSize >
            SnapshotCachePolicy.MaximumPageSize)
        {
            return Validation<PagedResult<SnapshotRecord>>(
                "Page size must be between 1 and 1,000,000.");
        }

        var totalPages = entry.RecordCount == 0
            ? 0
            : (entry.RecordCount + pageSize - 1) /
              pageSize;

        if ((totalPages == 0 && pageNumber != 1) ||
            (totalPages > 0 && pageNumber > totalPages))
        {
            return Validation<PagedResult<SnapshotRecord>>(
                "Page number exceeds the snapshot page count.");
        }

        var start = checked(
            (pageNumber - 1) * pageSize);
        var itemCount = entry.RecordCount == 0
            ? 0
            : (int)Math.Min(
                pageSize,
                entry.RecordCount - start);
        var items = new SnapshotRecord[itemCount];

        for (var index = 0; index < itemCount; index++)
        {
            var sourceIndex = checked((int)start + index);
            var value = entry.ValueSize == 0
                ? ReadOnlyMemory<byte>.Empty
                : entry.Values.AsMemory(
                    checked(
                        sourceIndex *
                        entry.ValueSize),
                    entry.ValueSize);
            items[index] = new SnapshotRecord(
                new CandidateAddress(
                    entry.Addresses[sourceIndex]),
                value);
        }

        return Result<PagedResult<SnapshotRecord>>.Success(
            new PagedResult<SnapshotRecord>(
                items,
                pageNumber,
                pageSize,
                entry.RecordCount));
    }

    private long CalculateDiskBytes(
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        var directory = sessionId is null
            ? _pathService.TempDirectory
            : Path.Combine(
                _pathService.TempDirectory,
                sessionId.Value.ToString("D"));

        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;

        foreach (var path in Directory.EnumerateFiles(
            directory,
            "*",
            SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                total = checked(
                    total + new FileInfo(path).Length);
            }
            catch (FileNotFoundException)
            {
            }
        }

        return total;
    }

    private static Result<CacheEntry?> InvalidCacheEntry(
        string message)
    {
        return Result<CacheEntry?>.Failure(
            new Error(ErrorCode.InvalidState, message));
    }

    private static Result<SnapshotCacheUsage> CancelledUsage(
        OperationCanceledException exception)
    {
        return Result<SnapshotCacheUsage>.Failure(
            new Error(
                ErrorCode.Cancelled,
                "Updating the snapshot cache policy was cancelled.",
                exception));
    }

    private static Result<T> Validation<T>(
        string message)
    {
        return Result<T>.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private readonly record struct SnapshotCacheKey(
        Guid SessionId,
        int NodeId);

    private sealed class CacheEntry
    {
        public CacheEntry(
            SnapshotCacheKey key,
            SnapshotDescriptor snapshot,
            ulong[] addresses,
            byte[] values,
            DateTimeOffset lastAccessedAt)
        {
            Key = key;
            FormatVersion = snapshot.FormatVersion;
            RecordCount = snapshot.RecordCount;
            ValueSize = snapshot.ValueSize;
            Checksum = snapshot.Checksum;
            StorageKind = snapshot.StorageKind;
            Addresses = addresses;
            Values = values;
            MemoryBytes = checked(
                (long)addresses.Length * sizeof(ulong) +
                values.LongLength);
            LastAccessedAt = lastAccessedAt;
            RecencyNode = new LinkedListNode<SnapshotCacheKey>(
                key);
        }

        public SnapshotCacheKey Key { get; }

        public int FormatVersion { get; }

        public long RecordCount { get; }

        public int ValueSize { get; }

        public string Checksum { get; }

        public SnapshotStorageKind StorageKind { get; }

        public ulong[] Addresses { get; }

        public byte[] Values { get; }

        public long MemoryBytes { get; }

        public DateTimeOffset LastAccessedAt { get; set; }

        public LinkedListNode<SnapshotCacheKey> RecencyNode
        {
            get;
            set;
        }

        public bool Matches(SnapshotDescriptor snapshot)
        {
            return snapshot.SessionId == Key.SessionId &&
                   snapshot.NodeId == Key.NodeId &&
                   snapshot.FormatVersion == FormatVersion &&
                   snapshot.RecordCount == RecordCount &&
                   snapshot.ValueSize == ValueSize &&
                   snapshot.StorageKind == StorageKind &&
                   snapshot.Checksum.Equals(
                       Checksum,
                       StringComparison.OrdinalIgnoreCase);
        }

        public SnapshotCacheEntryInfo ToInfo()
        {
            return new SnapshotCacheEntryInfo(
                Key.SessionId,
                Key.NodeId,
                RecordCount,
                MemoryBytes,
                LastAccessedAt);
        }
    }
}

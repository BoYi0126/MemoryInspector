using System.Runtime.CompilerServices;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed class DurationFilterService(
    IMonitoringSessionService monitoringSessionService,
    IMemoryReaderService memoryReaderService,
    ISnapshotStorage snapshotStorage,
    IValueMatcher valueMatcher,
    TimeProvider timeProvider) : IDurationFilterService
{
    private const int MaximumRetainedWarnings = 100;
    private static readonly TimeSpan ProgressInterval =
        TimeSpan.FromMilliseconds(100);
    private readonly IMonitoringSessionService _monitoringSessionService =
        Guard.NotNull(monitoringSessionService);
    private readonly IMemoryReaderService _memoryReaderService =
        Guard.NotNull(memoryReaderService);
    private readonly ISnapshotStorage _snapshotStorage =
        Guard.NotNull(snapshotStorage);
    private readonly IValueMatcher _valueMatcher =
        Guard.NotNull(valueMatcher);
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);

    public Task<Result<DurationFilterResult>> FilterAsync(
        DurationFilterRequest request,
        DurationFilterExecutionControl? executionControl = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => FilterCoreAsync(
                request,
                executionControl ??
                    new DurationFilterExecutionControl(),
                progress,
                cancellationToken),
            CancellationToken.None);
    }

    private async Task<Result<DurationFilterResult>> FilterCoreAsync(
        DurationFilterRequest request,
        DurationFilterExecutionControl executionControl,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation(
                "A Duration Filter request is required.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        var session = _monitoringSessionService.CurrentSession;

        if (session?.State != MonitoringSessionState.Connected ||
            session.SessionId != request.PreviousSnapshot.SessionId)
        {
            return Result<DurationFilterResult>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "The previous snapshot must belong to the " +
                    "connected monitoring session."));
        }

        var matcherResult = CreateMatchers(request.Filter);

        if (matcherResult.IsFailure)
        {
            return Result<DurationFilterResult>.Failure(
                matcherResult.Error);
        }

        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        try
        {
            var openResult = await _snapshotStorage.OpenAsync(
                    request.PreviousSnapshot.SessionId,
                    request.PreviousSnapshot.NodeId,
                    operationCancellation.Token)
                .ConfigureAwait(false);

            if (openResult.IsFailure)
            {
                return Result<DurationFilterResult>.Failure(
                    openResult.Error);
            }

            if (!HasSameSnapshot(
                request.PreviousSnapshot,
                openResult.Value))
            {
                return Result<DurationFilterResult>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "The previous snapshot changed before " +
                        "Duration Filter."));
            }

            var state = new DurationFilterState();
            var startedAt = _timeProvider.GetUtcNow();
            Result<SnapshotDescriptor> writeResult;

            if (request.ObservationMode ==
                DurationFilterObservationMode.EndpointCompare)
            {
                await WaitForActiveDelayAsync(
                        request.Duration,
                        TimeSpan.Zero,
                        request.Duration,
                        session,
                        executionControl,
                        progress,
                        operationCancellation.Token)
                    .ConfigureAwait(false);
                state.SampleCount = 1;
                var records = FilterEndpointRecordsAsync(
                    request,
                    openResult.Value,
                    matcherResult.Value,
                    session,
                    state,
                    operationCancellation);
                writeResult = await WriteSnapshotAsync(
                        request,
                        session,
                        records,
                        operationCancellation.Token)
                    .ConfigureAwait(false);
            }
            else
            {
                var candidatesResult =
                    await LoadCandidatesAsync(
                            request,
                            openResult.Value,
                            operationCancellation.Token)
                        .ConfigureAwait(false);

                if (candidatesResult.IsFailure)
                {
                    return Result<DurationFilterResult>.Failure(
                        candidatesResult.Error);
                }

                var observeResult =
                    await ObserveContinuouslyAsync(
                            request,
                            candidatesResult.Value,
                            matcherResult.Value,
                            session,
                            executionControl,
                            state,
                            progress,
                            operationCancellation.Token)
                        .ConfigureAwait(false);

                if (observeResult.IsFailure)
                {
                    return Result<DurationFilterResult>.Failure(
                        observeResult.Error);
                }

                AggregateFlags(
                    candidatesResult.Value,
                    state);
                writeResult = await WriteSnapshotAsync(
                        request,
                        session,
                        MatchedRecordsAsync(
                            request,
                            candidatesResult.Value,
                            operationCancellation.Token),
                        operationCancellation.Token)
                    .ConfigureAwait(false);
            }

            if (state.FatalError is not null)
            {
                return Result<DurationFilterResult>.Failure(
                    state.FatalError);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled();
            }

            if (writeResult.IsFailure)
            {
                return Result<DurationFilterResult>.Failure(
                    writeResult.Error);
            }

            progress?.Report(
                new OperationProgress(
                    request.Duration.Ticks,
                    request.Duration.Ticks,
                    "Duration Filter complete"));
            var completedAt = _timeProvider.GetUtcNow();
            return Result<DurationFilterResult>.Success(
                new DurationFilterResult(
                    Guid.NewGuid(),
                    request,
                    writeResult.Value,
                    state.SampleCount,
                    state.FailedObservationCount,
                    state.HasChangedCount,
                    state.HasIncreasedCount,
                    state.HasDecreasedCount,
                    state.ReadFailedCandidateCount,
                    state.Warnings,
                    state.SuppressedWarningCount,
                    startedAt,
                    completedAt));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(exception);
        }
        catch (SessionChangedException exception)
        {
            return Result<DurationFilterResult>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "The monitoring session changed during " +
                    "Duration Filter.",
                    exception));
        }
        catch (OutOfMemoryException exception)
        {
            return Result<DurationFilterResult>.Failure(
                new Error(
                    ErrorCode.ResourceExhausted,
                    "Duration Filter could not allocate its " +
                    "observation state.",
                    exception));
        }
    }

    private async Task<Result<List<CandidateState>>>
        LoadCandidatesAsync(
            DurationFilterRequest request,
            SnapshotDescriptor snapshot,
            CancellationToken cancellationToken)
    {
        var candidates = snapshot.RecordCount <= int.MaxValue
            ? new List<CandidateState>(
                checked((int)snapshot.RecordCount))
            : [];
        var totalPages = CalculateTotalPages(
            snapshot.RecordCount,
            request.PageSize);

        for (long pageNumber = 1;
             pageNumber <= totalPages;
             pageNumber++)
        {
            var pageResult = await _snapshotStorage.ReadPageAsync(
                    snapshot,
                    pageNumber,
                    request.PageSize,
                    cancellationToken)
                .ConfigureAwait(false);

            if (pageResult.IsFailure)
            {
                return Result<List<CandidateState>>.Failure(
                    pageResult.Error);
            }

            foreach (var record in pageResult.Value.Items)
            {
                if (record.Value.Length !=
                    request.Filter.ValueSize)
                {
                    return Result<List<CandidateState>>.Failure(
                        new Error(
                            ErrorCode.Serialization,
                            "Previous candidate value size is invalid."));
                }

                candidates.Add(
                    new CandidateState(
                        record.Candidate,
                        record.Value.ToArray()));
            }
        }

        return Result<List<CandidateState>>.Success(candidates);
    }

    private async Task<Result> ObserveContinuouslyAsync(
        DurationFilterRequest request,
        IReadOnlyList<CandidateState> candidates,
        MatcherSet matchers,
        MonitoringSession session,
        DurationFilterExecutionControl executionControl,
        DurationFilterState state,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var elapsed = TimeSpan.Zero;

        while (elapsed < request.Duration)
        {
            var delay = request.SampleInterval <
                request.Duration - elapsed
                ? request.SampleInterval
                : request.Duration - elapsed;
            await WaitForActiveDelayAsync(
                    delay,
                    elapsed,
                    request.Duration,
                    session,
                    executionControl,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            elapsed += delay;
            var sampleResult = await ObserveSampleAsync(
                    request,
                    candidates,
                    matchers,
                    session,
                    state,
                    cancellationToken)
                .ConfigureAwait(false);

            if (sampleResult.IsFailure)
            {
                return sampleResult;
            }

            state.SampleCount++;
        }

        return Result.Success();
    }

    private async Task<Result> ObserveSampleAsync(
        DurationFilterRequest request,
        IReadOnlyList<CandidateState> candidates,
        MatcherSet matchers,
        MonitoringSession session,
        DurationFilterState state,
        CancellationToken cancellationToken)
    {
        for (var offset = 0;
             offset < candidates.Count;
             offset += request.PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrent(session))
            {
                return Result.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "The monitoring session changed during " +
                        "Duration Filter."));
            }

            var count = Math.Min(
                request.PageSize,
                candidates.Count - offset);
            var requests = new MemoryReadRequest[count];

            for (var index = 0; index < count; index++)
            {
                requests[index] = new MemoryReadRequest(
                    candidates[offset + index].Candidate.Address,
                    request.Filter.ValueSize);
            }

            var batchResult = await _memoryReaderService
                .ReadBatchAsync(
                    requests,
                    new MemoryReadOptions(
                        request.Filter.ValueSize),
                    cancellationToken)
                .ConfigureAwait(false);

            if (batchResult.IsFailure)
            {
                return Result.Failure(batchResult.Error);
            }

            if (batchResult.Value.Items.Count != count)
            {
                return Result.Failure(
                    new Error(
                        ErrorCode.Unexpected,
                        "Batch read result count did not match " +
                        "the duration candidate page."));
            }

            for (var index = 0; index < count; index++)
            {
                var candidate = candidates[offset + index];
                var item = batchResult.Value.Items[index];

                if (item.Request.Address !=
                    candidate.Candidate.Address)
                {
                    return Result.Failure(
                        new Error(
                            ErrorCode.Unexpected,
                            "Batch read order did not match " +
                            "the duration candidate page."));
                }

                if (!TryGetCompleteValue(
                    item,
                    request.Filter.ValueSize,
                    state,
                    out var current))
                {
                    candidate.Flags |=
                        DurationObservationFlags.ReadFailed;
                    continue;
                }

                candidate.Update(current, matchers);
            }
        }

        return Result.Success();
    }

    private async IAsyncEnumerable<SnapshotRecord>
        FilterEndpointRecordsAsync(
            DurationFilterRequest request,
            SnapshotDescriptor previousSnapshot,
            MatcherSet matchers,
            MonitoringSession session,
            DurationFilterState state,
            CancellationTokenSource operationCancellation,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        var totalPages = CalculateTotalPages(
            previousSnapshot.RecordCount,
            request.PageSize);

        for (long pageNumber = 1;
             pageNumber <= totalPages;
             pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrent(session))
            {
                Abort(
                    state,
                    new Error(
                        ErrorCode.InvalidState,
                        "The monitoring session changed during " +
                        "Duration Filter."),
                    operationCancellation);
                yield break;
            }

            var pageResult = await _snapshotStorage.ReadPageAsync(
                    previousSnapshot,
                    pageNumber,
                    request.PageSize,
                    cancellationToken)
                .ConfigureAwait(false);

            if (pageResult.IsFailure)
            {
                Abort(
                    state,
                    pageResult.Error,
                    operationCancellation);
                yield break;
            }

            var previousRecords = pageResult.Value.Items;
            var requests = previousRecords
                .Select(record =>
                    new MemoryReadRequest(
                        record.Candidate.Address,
                        request.Filter.ValueSize))
                .ToArray();
            var batchResult = await _memoryReaderService
                .ReadBatchAsync(
                    requests,
                    new MemoryReadOptions(
                        request.Filter.ValueSize),
                    cancellationToken)
                .ConfigureAwait(false);

            if (batchResult.IsFailure)
            {
                Abort(
                    state,
                    batchResult.Error,
                    operationCancellation);
                yield break;
            }

            if (batchResult.Value.Items.Count !=
                previousRecords.Count)
            {
                Abort(
                    state,
                    new Error(
                        ErrorCode.Unexpected,
                        "Batch read result count did not match " +
                        "the duration candidate page."),
                    operationCancellation);
                yield break;
            }

            for (var index = 0;
                 index < previousRecords.Count;
                 index++)
            {
                var previous = previousRecords[index];
                var item = batchResult.Value.Items[index];

                if (item.Request.Address !=
                    previous.Candidate.Address)
                {
                    Abort(
                        state,
                        new Error(
                            ErrorCode.Unexpected,
                            "Batch read order did not match " +
                            "the duration candidate page."),
                        operationCancellation);
                    yield break;
                }

                if (previous.Value.Length !=
                    request.Filter.ValueSize)
                {
                    Abort(
                        state,
                        new Error(
                            ErrorCode.Serialization,
                            "Previous candidate value size is invalid."),
                        operationCancellation);
                    yield break;
                }

                if (!TryGetCompleteValue(
                    item,
                    request.Filter.ValueSize,
                    state,
                    out var current))
                {
                    state.ReadFailedCandidateCount++;
                    continue;
                }

                var flags = matchers.Compare(
                    current.Span,
                    previous.Value.Span);
                state.Count(flags);

                if (IsMatch(
                    request.Filter.ComparisonMode,
                    flags))
                {
                    yield return new SnapshotRecord(
                        previous.Candidate,
                        current);
                }
            }
        }
    }

    private bool TryGetCompleteValue(
        MemoryBatchReadItem item,
        int valueSize,
        DurationFilterState state,
        out ReadOnlyMemory<byte> value)
    {
        if (item.Result.IsFailure)
        {
            state.FailedObservationCount++;
            state.AddWarning(
                item.Result.Error,
                MaximumRetainedWarnings);
            value = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        var read = item.Result.Value;

        if (!read.IsComplete || read.Data.Length != valueSize)
        {
            state.FailedObservationCount++;
            state.AddWarning(
                read.Warnings.FirstOrDefault() ??
                    new Error(
                        ErrorCode.NotFound,
                        $"A complete value could not be read at " +
                        $"0x{item.Request.Address:X}."),
                MaximumRetainedWarnings);
            value = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        value = read.Data;
        return true;
    }

    private async Task WaitForActiveDelayAsync(
        TimeSpan delay,
        TimeSpan elapsedBeforeDelay,
        TimeSpan totalDuration,
        MonitoringSession session,
        DurationFilterExecutionControl executionControl,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var remaining = delay;

        while (remaining > TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrent(session))
            {
                throw new SessionChangedException();
            }

            var controlState = executionControl.CaptureState();

            if (controlState.IsPaused)
            {
                ReportDurationProgress(
                    progress,
                    elapsedBeforeDelay + delay - remaining,
                    totalDuration,
                    "Duration Filter paused");
                await controlState.StateChanged
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var slice = remaining < ProgressInterval
                ? remaining
                : ProgressInterval;
            var started = _timeProvider.GetTimestamp();
            using var delayCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    controlState.PauseRequested);

            try
            {
                await Task.Delay(
                        slice,
                        _timeProvider,
                        delayCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (
                    controlState.PauseRequested
                        .IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested)
            {
            }

            var elapsed = _timeProvider.GetElapsedTime(
                started,
                _timeProvider.GetTimestamp());

            if (elapsed > remaining)
            {
                elapsed = remaining;
            }

            remaining -= elapsed;
            ReportDurationProgress(
                progress,
                elapsedBeforeDelay + delay - remaining,
                totalDuration,
                "Observing duration");
        }
    }

    private Task<Result<SnapshotDescriptor>> WriteSnapshotAsync(
        DurationFilterRequest request,
        MonitoringSession session,
        IAsyncEnumerable<SnapshotRecord> records,
        CancellationToken cancellationToken)
    {
        return _snapshotStorage.WriteAsync(
            new SnapshotWriteRequest(
                session.SessionId,
                request.TargetNodeId,
                request.Filter.ValueType,
                includeValues: true),
            records,
            progress: null,
            cancellationToken);
    }

    private static async IAsyncEnumerable<SnapshotRecord>
        MatchedRecordsAsync(
            DurationFilterRequest request,
            IEnumerable<CandidateState> candidates,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsMatch(
                request.Filter.ComparisonMode,
                candidate.Flags))
            {
                yield return new SnapshotRecord(
                    candidate.Candidate,
                    candidate.CurrentValue);
            }
        }

        await Task.CompletedTask;
    }

    private Result<MatcherSet> CreateMatchers(ScanRequest filter)
    {
        var changed = _valueMatcher.CreatePairMatcher(
            filter.ValueType,
            ScanComparisonMode.Changed,
            filter.FloatingPointTolerance);
        var increased = _valueMatcher.CreatePairMatcher(
            filter.ValueType,
            ScanComparisonMode.Increased,
            filter.FloatingPointTolerance);
        var decreased = _valueMatcher.CreatePairMatcher(
            filter.ValueType,
            ScanComparisonMode.Decreased,
            filter.FloatingPointTolerance);

        if (changed.IsFailure)
        {
            return Result<MatcherSet>.Failure(changed.Error);
        }

        if (increased.IsFailure)
        {
            return Result<MatcherSet>.Failure(increased.Error);
        }

        if (decreased.IsFailure)
        {
            return Result<MatcherSet>.Failure(decreased.Error);
        }

        return Result<MatcherSet>.Success(
            new MatcherSet(
                changed.Value,
                increased.Value,
                decreased.Value));
    }

    private bool IsCurrent(MonitoringSession expected)
    {
        var current = _monitoringSessionService.CurrentSession;

        return current?.SessionId == expected.SessionId &&
               current.State == MonitoringSessionState.Connected &&
               current.Identity == expected.Identity;
    }

    private static bool HasSameSnapshot(
        SnapshotDescriptor expected,
        SnapshotDescriptor actual)
    {
        return expected.SessionId == actual.SessionId &&
               expected.NodeId == actual.NodeId &&
               expected.ValueType == actual.ValueType &&
               expected.RecordCount == actual.RecordCount &&
               expected.Checksum.Equals(
                   actual.Checksum,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static long CalculateTotalPages(
        long totalCount,
        int pageSize)
    {
        return totalCount / pageSize +
               (totalCount % pageSize == 0 ? 0 : 1);
    }

    private static bool IsMatch(
        ScanComparisonMode mode,
        DurationObservationFlags flags)
    {
        if (flags.HasFlag(DurationObservationFlags.ReadFailed))
        {
            return false;
        }

        return mode switch
        {
            ScanComparisonMode.Changed =>
                flags.HasFlag(
                    DurationObservationFlags.HasChanged),
            ScanComparisonMode.Unchanged =>
                !flags.HasFlag(
                    DurationObservationFlags.HasChanged),
            ScanComparisonMode.Increased =>
                flags.HasFlag(
                    DurationObservationFlags.HasIncreased),
            ScanComparisonMode.Decreased =>
                flags.HasFlag(
                    DurationObservationFlags.HasDecreased),
            _ => false,
        };
    }

    private static void AggregateFlags(
        IEnumerable<CandidateState> candidates,
        DurationFilterState state)
    {
        foreach (var candidate in candidates)
        {
            state.Count(candidate.Flags);

            if (candidate.Flags.HasFlag(
                DurationObservationFlags.ReadFailed))
            {
                state.ReadFailedCandidateCount++;
            }
        }
    }

    private static void ReportDurationProgress(
        IProgress<OperationProgress>? progress,
        TimeSpan completed,
        TimeSpan total,
        string stage)
    {
        var completedTicks = Math.Min(
            Math.Max(completed.Ticks, 0),
            total.Ticks);
        progress?.Report(
            new OperationProgress(
                completedTicks,
                total.Ticks,
                stage));
    }

    private static void Abort(
        DurationFilterState state,
        Error error,
        CancellationTokenSource cancellation)
    {
        state.FatalError = error;
        cancellation.Cancel();
    }

    private static Result<DurationFilterResult> Validation(
        string message)
    {
        return Result<DurationFilterResult>.Failure(
            new Error(
                ErrorCode.Validation,
                message));
    }

    private static Result<DurationFilterResult> Cancelled(
        Exception? exception = null)
    {
        return Result<DurationFilterResult>.Failure(
            new Error(
                ErrorCode.Cancelled,
                "Duration Filter was cancelled.",
                exception));
    }

    private sealed record MatcherSet(
        ScanValuePairMatcher Changed,
        ScanValuePairMatcher Increased,
        ScanValuePairMatcher Decreased)
    {
        public DurationObservationFlags Compare(
            ReadOnlySpan<byte> current,
            ReadOnlySpan<byte> previous)
        {
            var flags = DurationObservationFlags.None;

            if (Changed(current, previous))
            {
                flags |= DurationObservationFlags.HasChanged;
            }

            if (Increased(current, previous))
            {
                flags |= DurationObservationFlags.HasIncreased;
            }

            if (Decreased(current, previous))
            {
                flags |= DurationObservationFlags.HasDecreased;
            }

            return flags;
        }
    }

    private sealed class CandidateState(
        CandidateAddress candidate,
        byte[] initialValue)
    {
        public CandidateAddress Candidate { get; } = candidate;

        public byte[] CurrentValue { get; private set; } = initialValue;

        public DurationObservationFlags Flags { get; set; }

        public void Update(
            ReadOnlyMemory<byte> current,
            MatcherSet matchers)
        {
            Flags |= matchers.Compare(
                current.Span,
                CurrentValue);
            CurrentValue = current.ToArray();
        }
    }

    private sealed class DurationFilterState
    {
        public long SampleCount { get; set; }

        public long FailedObservationCount { get; set; }

        public long HasChangedCount { get; private set; }

        public long HasIncreasedCount { get; private set; }

        public long HasDecreasedCount { get; private set; }

        public long ReadFailedCandidateCount { get; set; }

        public List<Error> Warnings { get; } = [];

        public long SuppressedWarningCount { get; private set; }

        public Error? FatalError { get; set; }

        public void Count(DurationObservationFlags flags)
        {
            if (flags.HasFlag(
                DurationObservationFlags.HasChanged))
            {
                HasChangedCount++;
            }

            if (flags.HasFlag(
                DurationObservationFlags.HasIncreased))
            {
                HasIncreasedCount++;
            }

            if (flags.HasFlag(
                DurationObservationFlags.HasDecreased))
            {
                HasDecreasedCount++;
            }
        }

        public void AddWarning(
            Error error,
            int maximumRetainedWarnings)
        {
            if (Warnings.Count < maximumRetainedWarnings)
            {
                Warnings.Add(error);
            }
            else
            {
                SuppressedWarningCount++;
            }
        }
    }

    private sealed class SessionChangedException : Exception;
}

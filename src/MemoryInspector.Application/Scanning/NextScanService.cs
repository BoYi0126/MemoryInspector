using System.Runtime.CompilerServices;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed class NextScanService(
    IMonitoringSessionService monitoringSessionService,
    IMemoryReaderService memoryReaderService,
    ISnapshotStorage snapshotStorage,
    IValueMatcher valueMatcher,
    TimeProvider timeProvider) : INextScanService
{
    private const int MaximumRetainedWarnings = 100;
    private const string ProgressStage =
        "Filtering previous candidates";
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

    public Task<Result<NextScanResult>> ScanAsync(
        NextScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => ScanCoreAsync(
                request,
                progress,
                cancellationToken),
            CancellationToken.None);
    }

    private async Task<Result<NextScanResult>> ScanCoreAsync(
        NextScanRequest request,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation(
                "A Next Scan request is required.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        var session = _monitoringSessionService.CurrentSession;

        if (session?.State != MonitoringSessionState.Connected ||
            session.SessionId !=
            request.PreviousSnapshot.SessionId)
        {
            return Result<NextScanResult>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "The previous snapshot must belong to the " +
                    "connected monitoring session."));
        }

        var matcherResult = CreateMatcher(request.Filter);

        if (matcherResult.IsFailure)
        {
            return Result<NextScanResult>.Failure(
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
                return Result<NextScanResult>.Failure(
                    openResult.Error);
            }

            if (!HasSameSnapshot(
                request.PreviousSnapshot,
                openResult.Value))
            {
                return Result<NextScanResult>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "The previous snapshot changed before Next Scan."));
            }

            var state = new NextScanState();
            var startedAt = _timeProvider.GetUtcNow();
            var records = FilterRecordsAsync(
                request,
                openResult.Value,
                matcherResult.Value,
                session,
                state,
                progress,
                operationCancellation);
            var writeResult = await _snapshotStorage.WriteAsync(
                    new SnapshotWriteRequest(
                        session.SessionId,
                        request.TargetNodeId,
                        request.Filter.ValueType,
                        includeValues: true),
                    records,
                    progress: null,
                    operationCancellation.Token)
                .ConfigureAwait(false);

            if (state.FatalError is not null)
            {
                return Result<NextScanResult>.Failure(
                    state.FatalError);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled();
            }

            if (writeResult.IsFailure)
            {
                return Result<NextScanResult>.Failure(
                    writeResult.Error);
            }

            var completedAt = _timeProvider.GetUtcNow();
            return Result<NextScanResult>.Success(
                new NextScanResult(
                    Guid.NewGuid(),
                    request,
                    writeResult.Value,
                    state.ExaminedCount,
                    state.CompleteReadCount,
                    state.PartialReadCount,
                    state.FailedReadCount,
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
        catch (OutOfMemoryException exception)
        {
            return Result<NextScanResult>.Failure(
                new Error(
                    ErrorCode.ResourceExhausted,
                    "Next Scan could not allocate its page buffers.",
                    exception));
        }
    }

    private async IAsyncEnumerable<SnapshotRecord>
        FilterRecordsAsync(
            NextScanRequest request,
            SnapshotDescriptor previousSnapshot,
            MatcherPlan matcher,
            MonitoringSession session,
            NextScanState state,
            IProgress<OperationProgress>? progress,
            CancellationTokenSource operationCancellation,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        progress?.Report(
            new OperationProgress(
                0,
                previousSnapshot.RecordCount,
                ProgressStage));
        var totalPages = CalculateTotalPages(
            previousSnapshot.RecordCount,
            request.PageSize);

        for (long pageNumber = 1;
             pageNumber <= totalPages;
             pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            var readRequests = previousRecords
                .Select(record =>
                    new MemoryReadRequest(
                        record.Candidate.Address,
                        request.Filter.ValueSize))
                .ToArray();
            var batchResult = await _memoryReaderService
                .ReadBatchAsync(
                    readRequests,
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
                        "the previous candidate page."),
                    operationCancellation);
                yield break;
            }

            for (var index = 0;
                 index < previousRecords.Count;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = previousRecords[index];
                var item = batchResult.Value.Items[index];
                state.ExaminedCount++;

                if (item.Request.Address !=
                    previous.Candidate.Address)
                {
                    Abort(
                        state,
                        new Error(
                            ErrorCode.Unexpected,
                            "Batch read order did not match " +
                            "the previous candidate page."),
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

                if (item.Result.IsFailure)
                {
                    state.FailedReadCount++;
                    state.AddWarning(
                        item.Result.Error,
                        MaximumRetainedWarnings);
                    _ = new NextScanCandidateEvaluation(
                        previous.Candidate,
                        previous.Value,
                        ReadOnlyMemory<byte>.Empty,
                        IsMatch: false,
                        NextScanReadStatus.Failed,
                        item.Result.Error);
                    continue;
                }

                var read = item.Result.Value;

                if (!read.IsComplete ||
                    read.Data.Length !=
                    request.Filter.ValueSize)
                {
                    state.PartialReadCount++;
                    var warning = read.Warnings.FirstOrDefault() ??
                        new Error(
                            ErrorCode.NotFound,
                            $"A complete value could not be read at " +
                            $"0x{previous.Candidate.Address:X}.");
                    state.AddWarning(
                        warning,
                        MaximumRetainedWarnings);
                    _ = new NextScanCandidateEvaluation(
                        previous.Candidate,
                        previous.Value,
                        read.Data,
                        IsMatch: false,
                        NextScanReadStatus.Partial,
                        warning);
                    continue;
                }

                state.CompleteReadCount++;
                var isMatch = matcher.IsMatch(
                    read.Data.Span,
                    previous.Value.Span);
                var evaluation =
                    new NextScanCandidateEvaluation(
                        previous.Candidate,
                        previous.Value,
                        read.Data,
                        isMatch,
                        NextScanReadStatus.Complete,
                        ReadError: null);

                if (evaluation.IsMatch)
                {
                    yield return new SnapshotRecord(
                        evaluation.Candidate,
                        evaluation.CurrentValue);
                }
            }

            progress?.Report(
                new OperationProgress(
                    state.ExaminedCount,
                    previousSnapshot.RecordCount,
                    ProgressStage));
        }

        if (!IsCurrent(session))
        {
            Abort(
                state,
                new Error(
                    ErrorCode.InvalidState,
                    "The monitoring session changed during Next Scan."),
                operationCancellation);
        }
    }

    private Result<MatcherPlan> CreateMatcher(
        ScanRequest filter)
    {
        if (UsesFixedSearchValue(filter.ComparisonMode))
        {
            if (filter.SearchValue is null)
            {
                return Result<MatcherPlan>.Failure(
                    new Error(
                        ErrorCode.Validation,
                        $"{filter.ComparisonMode} requires a search value."));
            }

            var fixedResult = _valueMatcher.CreateMatcher(
                filter.SearchValue,
                filter.ComparisonMode,
                filter.FloatingPointTolerance);
            return fixedResult.IsSuccess
                ? Result<MatcherPlan>.Success(
                    new MatcherPlan(
                        fixedResult.Value,
                        PairMatcher: null))
                : Result<MatcherPlan>.Failure(
                    fixedResult.Error);
        }

        var pairResult = _valueMatcher.CreatePairMatcher(
            filter.ValueType,
            filter.ComparisonMode,
            filter.FloatingPointTolerance);
        return pairResult.IsSuccess
            ? Result<MatcherPlan>.Success(
                new MatcherPlan(
                    FixedMatcher: null,
                    pairResult.Value))
            : Result<MatcherPlan>.Failure(
                pairResult.Error);
    }

    private bool IsCurrent(MonitoringSession expected)
    {
        var current = _monitoringSessionService.CurrentSession;

        return current?.SessionId == expected.SessionId &&
               current.State == MonitoringSessionState.Connected &&
               current.Identity == expected.Identity;
    }

    private static bool UsesFixedSearchValue(
        ScanComparisonMode mode)
    {
        return mode is
            ScanComparisonMode.ExactValue or
            ScanComparisonMode.GreaterThan or
            ScanComparisonMode.LessThan;
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

    private static void Abort(
        NextScanState state,
        Error error,
        CancellationTokenSource cancellation)
    {
        state.FatalError = error;
        cancellation.Cancel();
    }

    private static Result<NextScanResult> Validation(
        string message)
    {
        return Result<NextScanResult>.Failure(
            new Error(
                ErrorCode.Validation,
                message));
    }

    private static Result<NextScanResult> Cancelled(
        Exception? exception = null)
    {
        return Result<NextScanResult>.Failure(
            new Error(
                ErrorCode.Cancelled,
                "Next Scan was cancelled.",
                exception));
    }

    private sealed record MatcherPlan(
        ScanValueMatcher? FixedMatcher,
        ScanValuePairMatcher? PairMatcher)
    {
        public bool IsMatch(
            ReadOnlySpan<byte> current,
            ReadOnlySpan<byte> previous)
        {
            return FixedMatcher?.Invoke(current) ??
                   PairMatcher!(current, previous);
        }
    }

    private sealed class NextScanState
    {
        public long ExaminedCount { get; set; }

        public long CompleteReadCount { get; set; }

        public long PartialReadCount { get; set; }

        public long FailedReadCount { get; set; }

        public List<Error> Warnings { get; } = [];

        public long SuppressedWarningCount { get; private set; }

        public Error? FatalError { get; set; }

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
}

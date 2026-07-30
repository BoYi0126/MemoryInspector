using System.Runtime.CompilerServices;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed class ExactInitialSnapshotService(
    IMonitoringSessionService monitoringSessionService,
    IMemoryRegionService memoryRegionService,
    IMemoryReaderService memoryReaderService,
    IValueMatcher valueMatcher,
    ISnapshotStorage snapshotStorage,
    TimeProvider timeProvider) : IExactInitialSnapshotService
{
    private const string ProgressStage = "Scanning memory";
    private readonly IMonitoringSessionService _sessionService =
        Guard.NotNull(monitoringSessionService);
    private readonly IMemoryRegionService _regionService =
        Guard.NotNull(memoryRegionService);
    private readonly IMemoryReaderService _readerService =
        Guard.NotNull(memoryReaderService);
    private readonly IValueMatcher _valueMatcher =
        Guard.NotNull(valueMatcher);
    private readonly ISnapshotStorage _snapshotStorage =
        Guard.NotNull(snapshotStorage);
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);

    public Task<Result<ExactInitialScanResult>> CreateSnapshotAsync(
        ExactInitialScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => CreateCoreAsync(
                request,
                progress,
                cancellationToken),
            CancellationToken.None);
    }

    private async Task<Result<ExactInitialScanResult>> CreateCoreAsync(
        ExactInitialScanRequest request,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Failure(
                ErrorCode.Validation,
                "An exact initial scan request is required.");
        }

        var session = _sessionService.CurrentSession;

        if (session?.State != MonitoringSessionState.Connected)
        {
            return Failure(
                ErrorCode.InvalidState,
                "A connected monitoring session is required.");
        }

        var matcher = _valueMatcher.CreateMatcher(
            request.ScanRequest.SearchValue!,
            ScanComparisonMode.ExactValue,
            request.ScanRequest.FloatingPointTolerance);

        if (matcher.IsFailure)
        {
            return Result<ExactInitialScanResult>.Failure(
                matcher.Error);
        }

        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        try
        {
            var regionResult = await _regionService
                .GetRegionsAsync(operationCancellation.Token)
                .ConfigureAwait(false);

            if (regionResult.IsFailure)
            {
                return Result<ExactInitialScanResult>.Failure(
                    regionResult.Error);
            }

            if (!IsCurrent(session))
            {
                return SessionChanged();
            }

            var allRegions = regionResult.Value.Regions
                .OrderBy(region => region.BaseAddress)
                .ToArray();
            var regions = allRegions
                .Where(region =>
                    region.IsReadable &&
                    region.Size >=
                        (ulong)request.ScanRequest.ValueSize)
                .ToArray();
            var state = new CaptureState(
                regionResult.Value.Warnings,
                CalculateTotalBytes(regions));
            var startedAt = _timeProvider.GetUtcNow();
            var records = CaptureRecordsAsync(
                session,
                request,
                regions,
                matcher.Value,
                state,
                progress,
                operationCancellation);
            var write = await _snapshotStorage.WriteAsync(
                    new SnapshotWriteRequest(
                        session.SessionId,
                        request.NodeId,
                        request.ScanRequest.ValueType,
                        includeValues: true),
                    records,
                    progress: null,
                    operationCancellation.Token)
                .ConfigureAwait(false);

            if (state.FatalError is not null)
            {
                return Result<ExactInitialScanResult>.Failure(
                    state.FatalError);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled();
            }

            if (write.IsFailure)
            {
                return Result<ExactInitialScanResult>.Failure(
                    write.Error);
            }

            if (!IsCurrent(session))
            {
                _ = await _snapshotStorage.DeleteAsync(
                        session.SessionId,
                        request.NodeId,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return SessionChanged();
            }

            if (state.IsResultLimitReached)
            {
                state.Warnings.Add(
                    new Error(
                        ErrorCode.ResourceExhausted,
                        $"The scan stopped after reaching the " +
                        $"{request.ScanRequest.MaximumResults:N0}-result limit."));
            }

            return Result<ExactInitialScanResult>.Success(
                new ExactInitialScanResult(
                    write.Value,
                    state.ScannedBytes,
                    state.ScannedRegionCount,
                    allRegions.Length - regions.Length,
                    state.IsResultLimitReached,
                    Array.AsReadOnly(state.Warnings.ToArray()),
                    startedAt,
                    _timeProvider.GetUtcNow()));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(exception);
        }
        catch (OutOfMemoryException exception)
        {
            return Failure(
                ErrorCode.ResourceExhausted,
                "The exact initial scan could not allocate candidate storage.",
                exception);
        }
    }

    private async IAsyncEnumerable<SnapshotRecord> CaptureRecordsAsync(
        MonitoringSession session,
        ExactInitialScanRequest request,
        IReadOnlyList<MemoryRegion> regions,
        ScanValueMatcher matcher,
        CaptureState state,
        IProgress<OperationProgress>? progress,
        CancellationTokenSource operationCancellation,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var mayOverlap = RegionsMayOverlap(regions);
        HashSet<ulong>? emittedAddresses =
            mayOverlap ? [] : null;
        long processedBytes = 0;
        progress?.Report(
            new OperationProgress(
                0,
                state.TotalBytes,
                ProgressStage));

        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.ScannedRegionCount++;
            ulong regionOffset = 0;
            ulong highestReadEnd = region.BaseAddress;

            while (regionOffset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = region.Size - regionOffset;
                var readLength = (int)Math.Min(
                    (ulong)request.ChunkSizeBytes,
                    remaining);
                var address = region.BaseAddress + regionOffset;
                var read = await _readerService.ReadAsync(
                        address,
                        readLength,
                        new MemoryReadOptions(readLength),
                        cancellationToken)
                    .ConfigureAwait(false);
                var isLast = (ulong)readLength == remaining;
                var advance = isLast
                    ? (ulong)readLength
                    : (ulong)(
                        readLength -
                        (request.ScanRequest.ValueSize - 1));

                if (read.IsFailure)
                {
                    if (IsFatal(read.Error))
                    {
                        state.FatalError = read.Error;
                        operationCancellation.Cancel();
                        yield break;
                    }

                    state.Warnings.Add(read.Error);
                }
                else
                {
                    state.Warnings.AddRange(read.Value.Warnings);
                    state.ScannedBytes = AddSaturated(
                        state.ScannedBytes,
                        CountNewBytes(
                            address,
                            read.Value.BytesRead,
                            ref highestReadEnd));
                    var data = read.Value.Data;
                    var step = request.ScanRequest.AddressStep;
                    var firstOffset =
                        request.ScanRequest.AlignmentMode ==
                        ScanAlignmentMode.Unaligned
                            ? 0
                            : GetAlignmentOffset(address, step);
                    var lastOffset =
                        data.Length - request.ScanRequest.ValueSize;

                    for (var offset = firstOffset;
                         offset <= lastOffset;
                         offset += step)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var candidateAddress =
                            address + (ulong)offset;
                        var candidateValue = data.Slice(
                            offset,
                            request.ScanRequest.ValueSize);

                        if (!matcher(candidateValue.Span) ||
                            (emittedAddresses is not null &&
                             !emittedAddresses.Add(candidateAddress)))
                        {
                            continue;
                        }

                        yield return new SnapshotRecord(
                            new CandidateAddress(candidateAddress),
                            candidateValue);
                        state.MatchedCount++;

                        if (state.MatchedCount >=
                            request.ScanRequest.MaximumResults)
                        {
                            state.IsResultLimitReached = true;
                            yield break;
                        }
                    }
                }

                regionOffset += advance;
                processedBytes = AddSaturated(
                    processedBytes,
                    (long)advance);
                progress?.Report(
                    new OperationProgress(
                        Math.Min(processedBytes, state.TotalBytes),
                        state.TotalBytes,
                        ProgressStage));
            }
        }

        if (!IsCurrent(session))
        {
            state.FatalError = new Error(
                ErrorCode.InvalidState,
                "The monitoring session changed during the scan.");
            operationCancellation.Cancel();
        }
    }

    private bool IsCurrent(MonitoringSession expected)
    {
        var current = _sessionService.CurrentSession;
        return current?.SessionId == expected.SessionId &&
               current.State == MonitoringSessionState.Connected &&
               current.Identity == expected.Identity;
    }

    private static bool IsFatal(Error error) =>
        error.Code is
            ErrorCode.Cancelled or
            ErrorCode.InvalidState or
            ErrorCode.ResourceExhausted;

    private static bool RegionsMayOverlap(
        IReadOnlyList<MemoryRegion> regions)
    {
        ulong highestEnd = 0;

        foreach (var region in regions)
        {
            if (region.BaseAddress < highestEnd)
            {
                return true;
            }

            highestEnd = Math.Max(highestEnd, region.EndAddress);
        }

        return false;
    }

    private static long CalculateTotalBytes(
        IEnumerable<MemoryRegion> regions)
    {
        long total = 0;

        foreach (var region in regions)
        {
            total = AddSaturated(
                total,
                region.Size > long.MaxValue
                    ? long.MaxValue
                    : (long)region.Size);
        }

        return total;
    }

    private static int GetAlignmentOffset(
        ulong address,
        int alignment)
    {
        var remainder = address % (ulong)alignment;
        return remainder == 0
            ? 0
            : (int)((ulong)alignment - remainder);
    }

    private static long CountNewBytes(
        ulong address,
        int bytesRead,
        ref ulong highestReadEnd)
    {
        var end = address + (ulong)bytesRead;
        var start = Math.Max(address, highestReadEnd);
        var count = end > start ? end - start : 0;
        highestReadEnd = Math.Max(highestReadEnd, end);
        return count > long.MaxValue
            ? long.MaxValue
            : (long)count;
    }

    private static long AddSaturated(long left, long right) =>
        left > long.MaxValue - right
            ? long.MaxValue
            : left + right;

    private static Result<ExactInitialScanResult> SessionChanged() =>
        Failure(
            ErrorCode.InvalidState,
            "The monitoring session changed during the scan.");

    private static Result<ExactInitialScanResult> Cancelled(
        Exception? exception = null) =>
        Failure(
            ErrorCode.Cancelled,
            "The exact initial scan was cancelled.",
            exception);

    private static Result<ExactInitialScanResult> Failure(
        ErrorCode code,
        string message,
        Exception? exception = null) =>
        Result<ExactInitialScanResult>.Failure(
            new Error(code, message, exception));

    private sealed class CaptureState(
        IEnumerable<Error> warnings,
        long totalBytes)
    {
        public List<Error> Warnings { get; } = [.. warnings];

        public long TotalBytes { get; } = totalBytes;

        public long ScannedBytes { get; set; }

        public long MatchedCount { get; set; }

        public int ScannedRegionCount { get; set; }

        public bool IsResultLimitReached { get; set; }

        public Error? FatalError { get; set; }
    }
}

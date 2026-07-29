using System.Runtime.CompilerServices;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed class UnknownInitialScanService(
    IMonitoringSessionService monitoringSessionService,
    IMemoryRegionService memoryRegionService,
    IMemoryReaderService memoryReaderService,
    ISnapshotStorage snapshotStorage,
    ISettingsService settingsService,
    TimeProvider timeProvider) : IUnknownInitialScanService
{
    private const string ProgressStage =
        "Capturing unknown initial values";
    private readonly IMonitoringSessionService _monitoringSessionService =
        Guard.NotNull(monitoringSessionService);
    private readonly IMemoryRegionService _memoryRegionService =
        Guard.NotNull(memoryRegionService);
    private readonly IMemoryReaderService _memoryReaderService =
        Guard.NotNull(memoryReaderService);
    private readonly ISnapshotStorage _snapshotStorage =
        Guard.NotNull(snapshotStorage);
    private readonly ISettingsService _settingsService =
        Guard.NotNull(settingsService);
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);

    public Task<Result<UnknownInitialScanEstimate>> EstimateAsync(
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => EstimateCoreAsync(
                valueType,
                alignmentMode,
                cancellationToken),
            CancellationToken.None);
    }

    public Task<Result<UnknownInitialScanResult>> CreateSnapshotAsync(
        UnknownInitialScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => CreateSnapshotCoreAsync(
                request,
                progress,
                cancellationToken),
            CancellationToken.None);
    }

    private async Task<Result<UnknownInitialScanEstimate>>
        EstimateCoreAsync(
            ScanValueType valueType,
            ScanAlignmentMode alignmentMode,
            CancellationToken cancellationToken)
    {
        var validation = ValidateSelection(
            valueType,
            alignmentMode);

        if (validation.IsFailure)
        {
            return Result<UnknownInitialScanEstimate>.Failure(
                validation.Error);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled<UnknownInitialScanEstimate>();
        }

        try
        {
            var contextResult = await LoadContextAsync(
                    valueType,
                    alignmentMode,
                    cancellationToken)
                .ConfigureAwait(false);

            return contextResult.IsSuccess
                ? Result<UnknownInitialScanEstimate>.Success(
                    contextResult.Value.Estimate)
                : Result<UnknownInitialScanEstimate>.Failure(
                    contextResult.Error);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled<UnknownInitialScanEstimate>(
                exception);
        }
    }

    private async Task<Result<UnknownInitialScanResult>>
        CreateSnapshotCoreAsync(
            UnknownInitialScanRequest request,
            IProgress<OperationProgress>? progress,
            CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation<UnknownInitialScanResult>(
                "An unknown-initial scan request is required.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled<UnknownInitialScanResult>();
        }

        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        try
        {
            var contextResult = await LoadContextAsync(
                    request.ValueType,
                    request.AlignmentMode,
                    operationCancellation.Token)
                .ConfigureAwait(false);

            if (contextResult.IsFailure)
            {
                return Result<UnknownInitialScanResult>.Failure(
                    contextResult.Error);
            }

            var context = contextResult.Value;
            var state = new CaptureState(
                context.RegionWarnings);
            var startedAt = _timeProvider.GetUtcNow();
            var writeRequest = new SnapshotWriteRequest(
                context.Session.SessionId,
                request.NodeId,
                request.ValueType,
                includeValues: true);
            var records = CaptureRecordsAsync(
                context,
                request,
                state,
                progress,
                operationCancellation);
            var writeResult = await _snapshotStorage.WriteAsync(
                    writeRequest,
                    records,
                    progress: null,
                    operationCancellation.Token)
                .ConfigureAwait(false);

            if (state.FatalError is not null)
            {
                return Result<UnknownInitialScanResult>.Failure(
                    state.FatalError);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled<UnknownInitialScanResult>();
            }

            if (writeResult.IsFailure)
            {
                return Result<UnknownInitialScanResult>.Failure(
                    writeResult.Error);
            }

            if (writeResult.Value.RecordCount >
                context.Estimate.CandidateCount)
            {
                return Result<UnknownInitialScanResult>.Failure(
                    new Error(
                        ErrorCode.Unexpected,
                        "Captured candidate count exceeded its estimate."));
            }

            var completedAt = _timeProvider.GetUtcNow();
            return Result<UnknownInitialScanResult>.Success(
                new UnknownInitialScanResult(
                    Guid.NewGuid(),
                    context.Estimate,
                    writeResult.Value,
                    state.ScannedBytes,
                    state.Warnings,
                    startedAt,
                    completedAt));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled<UnknownInitialScanResult>(
                exception);
        }
        catch (OutOfMemoryException exception)
        {
            return Result<UnknownInitialScanResult>.Failure(
                new Error(
                    ErrorCode.ResourceExhausted,
                    "The unknown-initial scan could not allocate a chunk.",
                    exception));
        }
    }

    private async Task<Result<ScanContext>> LoadContextAsync(
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        CancellationToken cancellationToken)
    {
        var session = _monitoringSessionService.CurrentSession;

        if (session?.State != MonitoringSessionState.Connected)
        {
            return Result<ScanContext>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "A connected monitoring session is required."));
        }

        var settingsResult = await _settingsService
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settingsResult.IsFailure)
        {
            return Result<ScanContext>.Failure(
                settingsResult.Error);
        }

        var settingsValidation = settingsResult.Value.Validate();

        if (settingsValidation.IsFailure)
        {
            return Result<ScanContext>.Failure(
                settingsValidation.Error);
        }

        var regionResult = await _memoryRegionService
            .GetRegionsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (regionResult.IsFailure)
        {
            return Result<ScanContext>.Failure(
                regionResult.Error);
        }

        if (!IsCurrent(session))
        {
            return Result<ScanContext>.Failure(
                SessionChangedError());
        }

        var regions = regionResult.Value.Regions
            .OrderBy(region => region.BaseAddress)
            .ToArray();
        var valueSize = ScanValueTypeInfo.GetSize(valueType);
        var scannableRegions = regions
            .Where(region =>
                region.IsReadable &&
                region.Size >= (ulong)valueSize)
            .ToArray();
        var estimate = CreateEstimate(
            session.SessionId,
            valueType,
            alignmentMode,
            regions.Length,
            scannableRegions,
            settingsResult.Value);

        return Result<ScanContext>.Success(
            new ScanContext(
                session,
                scannableRegions,
                regionResult.Value.Warnings,
                estimate));
    }

    private async IAsyncEnumerable<SnapshotRecord>
        CaptureRecordsAsync(
            ScanContext context,
            UnknownInitialScanRequest request,
            CaptureState state,
            IProgress<OperationProgress>? progress,
            CancellationTokenSource operationCancellation,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        long processedBytes = 0;
        ulong? lastCandidateAddress = null;

        progress?.Report(
            new OperationProgress(
                0,
                context.Estimate.ScannableBytes,
                ProgressStage));

        foreach (var region in context.Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ulong regionOffset = 0;
            ulong highestReadEnd = region.BaseAddress;

            while (regionOffset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = region.Size - regionOffset;
                var readLength = (int)Math.Min(
                    (ulong)request.ChunkSizeBytes,
                    remaining);
                var chunkAddress =
                    region.BaseAddress + regionOffset;
                var readResult = await _memoryReaderService
                    .ReadAsync(
                        chunkAddress,
                        readLength,
                        new MemoryReadOptions(readLength),
                        cancellationToken)
                    .ConfigureAwait(false);
                var isLastChunk =
                    (ulong)readLength == remaining;
                var logicalAdvance = isLastChunk
                    ? (ulong)readLength
                    : (ulong)(
                        readLength -
                        (request.ValueSize - 1));

                if (readResult.IsFailure)
                {
                    if (IsFatal(readResult.Error))
                    {
                        state.FatalError = readResult.Error;
                        operationCancellation.Cancel();
                        yield break;
                    }

                    state.Warnings.Add(readResult.Error);
                }
                else
                {
                    var memoryRead = readResult.Value;
                    state.Warnings.AddRange(
                        memoryRead.Warnings);
                    state.ScannedBytes = AddSaturated(
                        state.ScannedBytes,
                        CountNewBytes(
                            chunkAddress,
                            memoryRead.BytesRead,
                            ref highestReadEnd));
                    var data = memoryRead.Data;
                    var firstOffset =
                        request.AlignmentMode ==
                        ScanAlignmentMode.Unaligned
                            ? 0
                            : GetAlignmentOffset(
                                chunkAddress,
                                request.AddressStep);
                    var lastOffset =
                        data.Length - request.ValueSize;

                    for (var offset = firstOffset;
                         offset <= lastOffset;
                         offset += request.AddressStep)
                    {
                        cancellationToken
                            .ThrowIfCancellationRequested();
                        var address =
                            chunkAddress + (ulong)offset;

                        if (lastCandidateAddress.HasValue &&
                            address <=
                            lastCandidateAddress.Value)
                        {
                            continue;
                        }

                        lastCandidateAddress = address;
                        yield return new SnapshotRecord(
                            new CandidateAddress(address),
                            data.Slice(
                                offset,
                                request.ValueSize));
                    }
                }

                regionOffset += logicalAdvance;
                processedBytes = AddSaturated(
                    processedBytes,
                    (long)logicalAdvance);
                progress?.Report(
                    new OperationProgress(
                        Math.Min(
                            processedBytes,
                            context.Estimate.ScannableBytes),
                        context.Estimate.ScannableBytes,
                        ProgressStage));
            }
        }

        if (!IsCurrent(context.Session))
        {
            state.FatalError = SessionChangedError();
            operationCancellation.Cancel();
        }
    }

    private static UnknownInitialScanEstimate CreateEstimate(
        Guid sessionId,
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        int totalRegionCount,
        IReadOnlyList<MemoryRegion> regions,
        AppSettings settings)
    {
        var valueSize = ScanValueTypeInfo.GetSize(valueType);
        var step = alignmentMode == ScanAlignmentMode.Aligned
            ? valueSize
            : 1;
        long candidateCount = 0;
        long scannableBytes = 0;
        ulong? highestCandidateAddress = null;

        foreach (var region in regions)
        {
            scannableBytes = AddSaturated(
                scannableBytes,
                region.Size > long.MaxValue
                    ? long.MaxValue
                    : (long)region.Size);

            if (!TryGetCandidateRange(
                region,
                valueSize,
                step,
                alignmentMode,
                out var firstAddress,
                out var lastAddress))
            {
                continue;
            }

            if (highestCandidateAddress.HasValue &&
                firstAddress <= highestCandidateAddress.Value)
            {
                if (highestCandidateAddress.Value >
                    ulong.MaxValue - (ulong)step)
                {
                    continue;
                }

                firstAddress =
                    highestCandidateAddress.Value + (ulong)step;
            }

            if (firstAddress > lastAddress)
            {
                continue;
            }

            var count =
                (lastAddress - firstAddress) / (ulong)step + 1;
            candidateCount = AddSaturated(
                candidateCount,
                count > long.MaxValue
                    ? long.MaxValue
                    : (long)count);
            highestCandidateAddress = Math.Max(
                highestCandidateAddress ?? 0,
                lastAddress);
        }

        var recordSize =
            SnapshotFormatInfo.AddressSize + valueSize;
        var payloadBytes = MultiplySaturated(
            candidateCount,
            recordSize);
        var estimatedDiskBytes = AddSaturated(
            SnapshotFormatInfo.HeaderSize,
            payloadBytes);

        return new UnknownInitialScanEstimate(
            sessionId,
            valueType,
            alignmentMode,
            candidateCount,
            scannableBytes,
            estimatedDiskBytes,
            settings.MemoryBudgetBytes,
            settings.SnapshotThreshold,
            regions.Count,
            totalRegionCount - regions.Count);
    }

    private static bool TryGetCandidateRange(
        MemoryRegion region,
        int valueSize,
        int step,
        ScanAlignmentMode alignmentMode,
        out ulong firstAddress,
        out ulong lastAddress)
    {
        firstAddress = region.BaseAddress;

        if (alignmentMode == ScanAlignmentMode.Aligned)
        {
            var offset = GetAlignmentOffset(
                region.BaseAddress,
                step);
            firstAddress += (ulong)offset;
        }

        lastAddress =
            region.EndAddress - (ulong)valueSize;
        return firstAddress <= lastAddress;
    }

    private bool IsCurrent(MonitoringSession expected)
    {
        var current = _monitoringSessionService.CurrentSession;

        return current?.SessionId == expected.SessionId &&
               current.State == MonitoringSessionState.Connected &&
               current.Identity == expected.Identity;
    }

    private static Result ValidateSelection(
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode)
    {
        try
        {
            _ = ScanValueTypeInfo.GetSize(valueType);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "A supported value type must be selected.",
                    exception));
        }

        return Enum.IsDefined(alignmentMode)
            ? Result.Success()
            : Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "A supported alignment mode must be selected."));
    }

    private static bool IsFatal(Error error)
    {
        return error.Code is
            ErrorCode.Cancelled or
            ErrorCode.InvalidState or
            ErrorCode.ResourceExhausted;
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
        var endAddress = address + (ulong)bytesRead;
        var newStart = Math.Max(address, highestReadEnd);
        var count = endAddress > newStart
            ? endAddress - newStart
            : 0;
        highestReadEnd = Math.Max(
            highestReadEnd,
            endAddress);
        return count > long.MaxValue
            ? long.MaxValue
            : (long)count;
    }

    private static long MultiplySaturated(
        long value,
        int multiplier)
    {
        return value > long.MaxValue / multiplier
            ? long.MaxValue
            : value * multiplier;
    }

    private static long AddSaturated(
        long left,
        long right)
    {
        return left > long.MaxValue - right
            ? long.MaxValue
            : left + right;
    }

    private static Error SessionChangedError()
    {
        return new Error(
            ErrorCode.InvalidState,
            "The monitoring session changed during snapshot capture.");
    }

    private static Result<T> Validation<T>(string message)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Validation,
                message));
    }

    private static Result<T> Cancelled<T>(
        Exception? exception = null)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Cancelled,
                "The unknown-initial scan was cancelled.",
                exception));
    }

    private sealed record ScanContext(
        MonitoringSession Session,
        IReadOnlyList<MemoryRegion> Regions,
        IReadOnlyList<Error> RegionWarnings,
        UnknownInitialScanEstimate Estimate);

    private sealed class CaptureState(
        IEnumerable<Error> warnings)
    {
        public List<Error> Warnings { get; } =
            warnings.ToList();

        public long ScannedBytes { get; set; }

        public Error? FatalError { get; set; }
    }
}

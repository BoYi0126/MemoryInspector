using MemoryInspector.Application.Memory;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed class ExactValueFirstScanService(
    IMemoryRegionService memoryRegionService,
    IMemoryReaderService memoryReaderService,
    IValueMatcher valueMatcher,
    TimeProvider timeProvider) : IFirstScanService
{
    private const string ProgressStage = "Scanning memory";
    private readonly IMemoryRegionService _memoryRegionService =
        Guard.NotNull(memoryRegionService);
    private readonly IMemoryReaderService _memoryReaderService =
        Guard.NotNull(memoryReaderService);
    private readonly IValueMatcher _valueMatcher =
        Guard.NotNull(valueMatcher);
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);

    public Task<Result<FirstScanResult>> ScanExactValueAsync(
        ScanRequest request,
        FirstScanOptions? options = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => ScanExactValueCoreAsync(
                request,
                options,
                progress,
                cancellationToken),
            CancellationToken.None);
    }

    private async Task<Result<FirstScanResult>> ScanExactValueCoreAsync(
        ScanRequest request,
        FirstScanOptions? options,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Validation("A scan request is required.");
        }

        if (request.ComparisonMode != ScanComparisonMode.ExactValue ||
            request.SearchValue is null)
        {
            return Validation(
                "A first exact-value scan requires an exact search value.");
        }

        options ??= new FirstScanOptions();

        if (options.ChunkSizeBytes < request.ValueSize)
        {
            return Validation(
                "Scan chunk size cannot be smaller than the value size.");
        }

        var matcherResult = _valueMatcher.CreateMatcher(
            request.SearchValue,
            ScanComparisonMode.ExactValue,
            request.FloatingPointTolerance);

        if (matcherResult.IsFailure)
        {
            return Result<FirstScanResult>.Failure(
                matcherResult.Error);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        try
        {
            var regionResult = await _memoryRegionService
                .GetRegionsAsync(cancellationToken)
                .ConfigureAwait(false);

            if (regionResult.IsFailure)
            {
                return Result<FirstScanResult>.Failure(
                    regionResult.Error);
            }

            var regions = regionResult.Value.Regions
                .OrderBy(region => region.BaseAddress)
                .ToArray();
            var scannableRegions = regions
                .Where(region => IsScannable(
                    region,
                    request.ValueSize))
                .ToArray();
            var totalBytes = CalculateTotalBytes(scannableRegions);
            var warnings = regionResult.Value.Warnings.ToList();
            var candidates = new List<CandidateAddress>(
                Math.Min(request.MaximumResults, 4096));
            var candidateAddresses = RegionsMayOverlap(
                scannableRegions)
                ? new HashSet<ulong>()
                : null;
            var startedAt = _timeProvider.GetUtcNow();
            long processedBytes = 0;
            long scannedBytes = 0;
            var scannedRegionCount = 0;
            var resultLimitReached = false;

            progress?.Report(
                new OperationProgress(
                    0,
                    totalBytes,
                    ProgressStage));

            foreach (var region in scannableRegions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scannedRegionCount++;
                ulong regionOffset = 0;
                ulong highestReadEnd = region.BaseAddress;

                while (regionOffset < region.Size)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var remaining = region.Size - regionOffset;
                    var requestLength = (int)Math.Min(
                        (ulong)options.ChunkSizeBytes,
                        remaining);
                    var chunkAddress =
                        region.BaseAddress + regionOffset;
                    var readResult = await _memoryReaderService
                        .ReadAsync(
                            chunkAddress,
                            requestLength,
                            new MemoryReadOptions(requestLength),
                            cancellationToken)
                        .ConfigureAwait(false);
                    var isLastChunk =
                        (ulong)requestLength == remaining;
                    var logicalAdvance = isLastChunk
                        ? (ulong)requestLength
                        : (ulong)(
                            requestLength -
                            (request.ValueSize - 1));

                    if (readResult.IsFailure)
                    {
                        if (IsFatal(readResult.Error))
                        {
                            return Result<FirstScanResult>.Failure(
                                readResult.Error);
                        }

                        warnings.Add(readResult.Error);
                    }
                    else
                    {
                        var memoryRead = readResult.Value;
                        warnings.AddRange(memoryRead.Warnings);
                        scannedBytes += CountNewBytes(
                            chunkAddress,
                            memoryRead.BytesRead,
                            ref highestReadEnd);
                        ScanChunk(
                            memoryRead.Data.Span,
                            chunkAddress,
                            request,
                            matcherResult.Value,
                            candidates,
                            candidateAddresses,
                            ref resultLimitReached);
                    }

                    regionOffset += logicalAdvance;
                    processedBytes = AddSaturated(
                        processedBytes,
                        (long)logicalAdvance);
                    progress?.Report(
                        new OperationProgress(
                            Math.Min(processedBytes, totalBytes),
                            totalBytes,
                            ProgressStage));

                    if (resultLimitReached)
                    {
                        break;
                    }
                }

                if (resultLimitReached)
                {
                    break;
                }
            }

            if (resultLimitReached)
            {
                warnings.Add(
                    new Error(
                        ErrorCode.ResourceExhausted,
                        $"The scan stopped after reaching the " +
                        $"{request.MaximumResults:N0}-result limit."));
            }

            var completedAt = _timeProvider.GetUtcNow();
            var skippedRegionCount =
                regions.Length - scannableRegions.Length;
            var isPartial =
                warnings.Count > 0 ||
                resultLimitReached;
            var summary = new ScanResult(
                Guid.NewGuid(),
                request,
                scannedBytes,
                candidates.Count,
                startedAt,
                completedAt,
                isPartial);

            return Result<FirstScanResult>.Success(
                new FirstScanResult(
                    summary,
                    candidates,
                    warnings,
                    scannedRegionCount,
                    skippedRegionCount,
                    resultLimitReached));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(exception);
        }
        catch (OutOfMemoryException exception)
        {
            return Result<FirstScanResult>.Failure(
                new Error(
                    ErrorCode.ResourceExhausted,
                    "The scan could not allocate candidate storage.",
                    exception));
        }
    }

    private static void ScanChunk(
        ReadOnlySpan<byte> data,
        ulong chunkAddress,
        ScanRequest request,
        ScanValueMatcher matcher,
        List<CandidateAddress> candidates,
        HashSet<ulong>? candidateAddresses,
        ref bool resultLimitReached)
    {
        if (data.Length < request.ValueSize)
        {
            return;
        }

        var firstOffset = request.AlignmentMode ==
            ScanAlignmentMode.Unaligned
            ? 0
            : GetAlignmentOffset(
                chunkAddress,
                request.AddressStep);
        var lastOffset = data.Length - request.ValueSize;

        for (var offset = firstOffset;
             offset <= lastOffset;
             offset += request.AddressStep)
        {
            var value = data.Slice(offset, request.ValueSize);

            if (!matcher(value))
            {
                continue;
            }

            var address = chunkAddress + (ulong)offset;

            if (candidateAddresses is not null &&
                !candidateAddresses.Add(address))
            {
                continue;
            }

            candidates.Add(new CandidateAddress(address));

            if (candidates.Count >= request.MaximumResults)
            {
                resultLimitReached = true;
                return;
            }
        }
    }

    private static bool IsScannable(
        MemoryRegion region,
        int valueSize)
    {
        return region.IsReadable &&
               region.Size >= (ulong)valueSize;
    }

    private static bool RegionsMayOverlap(
        IReadOnlyList<MemoryRegion> regions)
    {
        if (regions.Count < 2)
        {
            return false;
        }

        var highestEndAddress = regions[0].EndAddress;

        for (var index = 1; index < regions.Count; index++)
        {
            var region = regions[index];

            if (region.BaseAddress < highestEndAddress)
            {
                return true;
            }

            highestEndAddress = Math.Max(
                highestEndAddress,
                region.EndAddress);
        }

        return false;
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
        highestReadEnd = Math.Max(highestReadEnd, endAddress);
        return count > long.MaxValue
            ? long.MaxValue
            : (long)count;
    }

    private static long AddSaturated(
        long left,
        long right)
    {
        return left > long.MaxValue - right
            ? long.MaxValue
            : left + right;
    }

    private static Result<FirstScanResult> Validation(
        string message)
    {
        return Result<FirstScanResult>.Failure(
            new Error(
                ErrorCode.Validation,
                message));
    }

    private static Result<FirstScanResult> Cancelled(
        Exception? exception = null)
    {
        return Result<FirstScanResult>.Failure(
            new Error(
                ErrorCode.Cancelled,
                "The exact-value scan was cancelled.",
                exception));
    }
}

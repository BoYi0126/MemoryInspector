using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Snapshots.Comparison;

public sealed class SnapshotCompareService(
    ISnapshotStorage snapshotStorage) : ISnapshotCompareService
{
    public const int DefaultDifferencePageSize = 500;
    public const int MaximumDifferencePageSize = 10_000;
    internal const int StoragePageSize = 4_096;

    private readonly ISnapshotStorage _snapshotStorage =
        Guard.NotNull(snapshotStorage);

    public async Task<Result<SnapshotComparisonPage>> CompareAsync(
        SnapshotDescriptor left,
        SnapshotDescriptor right,
        long pageNumber = 1,
        int pageSize = DefaultDifferencePageSize,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber <= 0 ||
            pageSize <= 0 ||
            pageSize > MaximumDifferencePageSize)
        {
            return Validation<SnapshotComparisonPage>(
                "Comparison page bounds are invalid.");
        }

        long skip;

        try
        {
            skip = checked((pageNumber - 1) * pageSize);
        }
        catch (OverflowException exception)
        {
            return Result<SnapshotComparisonPage>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Comparison page bounds are invalid.",
                    exception));
        }

        var items = new List<SnapshotDifference>(pageSize);
        long differenceIndex = 0;
        var result = await StreamAsync(
            left,
            right,
            (kind, leftRecord, rightRecord, token) =>
            {
                if (differenceIndex >= skip &&
                    items.Count < pageSize)
                {
                    items.Add(
                        CreateDifference(
                            kind,
                            leftRecord,
                            rightRecord));
                }

                differenceIndex++;
                return ValueTask.CompletedTask;
            },
            progress,
            cancellationToken);

        if (result.IsFailure)
        {
            return Result<SnapshotComparisonPage>.Failure(
                result.Error);
        }

        var total = result.Value.TotalComparedAddressCount;
        var totalPages = total == 0
            ? 0
            : ((total - 1) / pageSize) + 1;

        if (totalPages > 0 && pageNumber > totalPages)
        {
            return Validation<SnapshotComparisonPage>(
                "Comparison page number exceeds the result set.");
        }

        return Result<SnapshotComparisonPage>.Success(
            new SnapshotComparisonPage(
                result.Value,
                new PagedResult<SnapshotDifference>(
                    items,
                    pageNumber,
                    pageSize,
                    total)));
    }

    public Task<Result<SnapshotComparisonSummary>> VisitAsync(
        SnapshotDescriptor left,
        SnapshotDescriptor right,
        Func<
            SnapshotDifference,
            CancellationToken,
            ValueTask> visitor,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (visitor is null)
        {
            return Task.FromResult(
                Validation<SnapshotComparisonSummary>(
                    "A comparison visitor is required."));
        }

        return StreamAsync(
            left,
            right,
            (kind, leftRecord, rightRecord, token) =>
                visitor(
                    CreateDifference(
                        kind,
                        leftRecord,
                        rightRecord),
                    token),
            progress,
            cancellationToken);
    }

    private async Task<Result<SnapshotComparisonSummary>> StreamAsync(
        SnapshotDescriptor left,
        SnapshotDescriptor right,
        Func<
            SnapshotDifferenceKind,
            SnapshotRecord?,
            SnapshotRecord?,
            CancellationToken,
            ValueTask> visitor,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var validation = ValidateSnapshots(left, right);

        if (validation.IsFailure)
        {
            return Result<SnapshotComparisonSummary>.Failure(
                validation.Error);
        }

        var leftCursor = new SnapshotCursor(
            _snapshotStorage,
            left);
        var rightCursor = new SnapshotCursor(
            _snapshotStorage,
            right);
        long added = 0;
        long removed = 0;
        long changed = 0;
        long unchanged = 0;
        long consumed = 0;
        var totalInput = left.RecordCount + right.RecordCount;

        try
        {
            var leftAdvance = await leftCursor.AdvanceAsync(
                cancellationToken);

            if (leftAdvance.IsFailure)
            {
                return Result<SnapshotComparisonSummary>.Failure(
                    leftAdvance.Error);
            }

            var rightAdvance = await rightCursor.AdvanceAsync(
                cancellationToken);

            if (rightAdvance.IsFailure)
            {
                return Result<SnapshotComparisonSummary>.Failure(
                    rightAdvance.Error);
            }

            while (leftCursor.HasCurrent || rightCursor.HasCurrent)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SnapshotDifferenceKind kind;
                SnapshotRecord? differenceLeft = null;
                SnapshotRecord? differenceRight = null;
                var consumedNow = 1L;

                if (!leftCursor.HasCurrent)
                {
                    kind = SnapshotDifferenceKind.Added;
                    differenceRight = rightCursor.Current;
                    added++;
                    rightAdvance = await rightCursor.AdvanceAsync(
                        cancellationToken);

                    if (rightAdvance.IsFailure)
                    {
                        return Failure(rightAdvance.Error);
                    }
                }
                else if (!rightCursor.HasCurrent)
                {
                    kind = SnapshotDifferenceKind.Removed;
                    differenceLeft = leftCursor.Current;
                    removed++;
                    leftAdvance = await leftCursor.AdvanceAsync(
                        cancellationToken);

                    if (leftAdvance.IsFailure)
                    {
                        return Failure(leftAdvance.Error);
                    }
                }
                else
                {
                    var leftAddress =
                        leftCursor.Current.Candidate.Address;
                    var rightAddress =
                        rightCursor.Current.Candidate.Address;

                    if (leftAddress < rightAddress)
                    {
                        kind = SnapshotDifferenceKind.Removed;
                        differenceLeft = leftCursor.Current;
                        removed++;
                        leftAdvance =
                            await leftCursor.AdvanceAsync(
                                cancellationToken);

                        if (leftAdvance.IsFailure)
                        {
                            return Failure(leftAdvance.Error);
                        }
                    }
                    else if (rightAddress < leftAddress)
                    {
                        kind = SnapshotDifferenceKind.Added;
                        differenceRight = rightCursor.Current;
                        added++;
                        rightAdvance =
                            await rightCursor.AdvanceAsync(
                                cancellationToken);

                        if (rightAdvance.IsFailure)
                        {
                            return Failure(rightAdvance.Error);
                        }
                    }
                    else
                    {
                        var isChanged =
                            !leftCursor.Current.Value.Span.SequenceEqual(
                                rightCursor.Current.Value.Span);
                        kind = isChanged
                            ? SnapshotDifferenceKind.Changed
                            : SnapshotDifferenceKind.Unchanged;
                        differenceLeft = leftCursor.Current;
                        differenceRight = rightCursor.Current;

                        if (isChanged)
                        {
                            changed++;
                        }
                        else
                        {
                            unchanged++;
                        }

                        consumedNow = 2;
                        leftAdvance =
                            await leftCursor.AdvanceAsync(
                                cancellationToken);

                        if (leftAdvance.IsFailure)
                        {
                            return Failure(leftAdvance.Error);
                        }

                        rightAdvance =
                            await rightCursor.AdvanceAsync(
                                cancellationToken);

                        if (rightAdvance.IsFailure)
                        {
                            return Failure(rightAdvance.Error);
                        }
                    }
                }

                await visitor(
                    kind,
                    differenceLeft,
                    differenceRight,
                    cancellationToken);
                consumed += consumedNow;

                if (consumed % StoragePageSize == 0 ||
                    consumed == totalInput)
                {
                    progress?.Report(
                        new OperationProgress(
                            consumed,
                            totalInput,
                            "Comparing snapshots"));
                }
            }

            progress?.Report(
                new OperationProgress(
                    totalInput,
                    totalInput,
                    "Comparison complete"));
            return Result<SnapshotComparisonSummary>.Success(
                new SnapshotComparisonSummary(
                    left,
                    right,
                    added,
                    removed,
                    changed,
                    unchanged));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<SnapshotComparisonSummary>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Snapshot comparison was cancelled.",
                    exception));
        }
        catch (Exception exception)
        {
            return Result<SnapshotComparisonSummary>.Failure(
                new Error(
                    ErrorCode.Unexpected,
                    "Snapshot comparison failed.",
                    exception));
        }

        Result<SnapshotComparisonSummary> Failure(Error error)
        {
            return Result<SnapshotComparisonSummary>.Failure(error);
        }
    }

    private static Result ValidateSnapshots(
        SnapshotDescriptor? left,
        SnapshotDescriptor? right)
    {
        if (left is null || right is null)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Two snapshots are required."));
        }

        if (left.ValueType != right.ValueType ||
            left.IncludesValues != right.IncludesValues ||
            left.ValueSize != right.ValueSize)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Snapshots must use the same value type and layout."));
        }

        if (left.RecordCount >
            long.MaxValue - right.RecordCount)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.ResourceExhausted,
                    "Combined snapshot record count is too large."));
        }

        return Result.Success();
    }

    private static SnapshotDifference CreateDifference(
        SnapshotDifferenceKind kind,
        SnapshotRecord? left,
        SnapshotRecord? right)
    {
        var address = left?.Candidate.Address ??
            right?.Candidate.Address ??
            throw new InvalidOperationException(
                "A comparison difference requires a record.");
        return new SnapshotDifference(
            address,
            kind,
            left?.Value,
            right?.Value);
    }

    private static Result<T> Validation<T>(string message)
    {
        return Result<T>.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private sealed class SnapshotCursor(
        ISnapshotStorage storage,
        SnapshotDescriptor snapshot)
    {
        private IReadOnlyList<SnapshotRecord> _page = [];
        private int _index = -1;
        private long _pageNumber;
        private long _readCount;
        private ulong? _previousAddress;

        public bool HasCurrent { get; private set; }

        public SnapshotRecord Current { get; private set; }

        public async Task<Result<bool>> AdvanceAsync(
            CancellationToken cancellationToken)
        {
            if (_readCount >= snapshot.RecordCount)
            {
                HasCurrent = false;
                return Result<bool>.Success(false);
            }

            _index++;

            if (_index >= _page.Count)
            {
                _pageNumber++;
                var pageResult = await storage.ReadPageAsync(
                    snapshot,
                    _pageNumber,
                    StoragePageSize,
                    cancellationToken);

                if (pageResult.IsFailure)
                {
                    return Result<bool>.Failure(
                        pageResult.Error);
                }

                if (pageResult.Value.TotalCount !=
                        snapshot.RecordCount ||
                    pageResult.Value.Items.Count == 0)
                {
                    return Result<bool>.Failure(
                        new Error(
                            ErrorCode.Serialization,
                            "Snapshot page metadata is inconsistent."));
                }

                _page = pageResult.Value.Items;
                _index = 0;
            }

            Current = _page[_index];
            var address = Current.Candidate.Address;

            if (Current.Value.Length != snapshot.ValueSize)
            {
                return Result<bool>.Failure(
                    new Error(
                        ErrorCode.Serialization,
                        "Snapshot record value layout is inconsistent."));
            }

            if (_previousAddress.HasValue &&
                address <= _previousAddress.Value)
            {
                return Result<bool>.Failure(
                    new Error(
                        ErrorCode.Serialization,
                        "Snapshot addresses are not strictly ordered."));
            }

            _previousAddress = address;
            _readCount++;
            HasCurrent = true;
            return Result<bool>.Success(true);
        }
    }
}

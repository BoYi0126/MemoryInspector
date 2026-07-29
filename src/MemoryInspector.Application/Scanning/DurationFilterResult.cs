using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning;

public sealed class DurationFilterResult
{
    public DurationFilterResult(
        Guid filterId,
        DurationFilterRequest request,
        SnapshotDescriptor snapshot,
        long sampleCount,
        long failedObservationCount,
        long hasChangedCount,
        long hasIncreasedCount,
        long hasDecreasedCount,
        long readFailedCandidateCount,
        IEnumerable<Error>? warnings,
        long suppressedWarningCount,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        if (filterId == Guid.Empty)
        {
            throw new ArgumentException(
                "Filter ID cannot be empty.",
                nameof(filterId));
        }

        Request = request ??
            throw new ArgumentNullException(nameof(request));
        Snapshot = snapshot ??
            throw new ArgumentNullException(nameof(snapshot));

        if (snapshot.SessionId !=
                request.PreviousSnapshot.SessionId ||
            snapshot.NodeId != request.TargetNodeId ||
            snapshot.ValueType != request.Filter.ValueType ||
            !snapshot.IncludesValues)
        {
            throw new ArgumentException(
                "Duration snapshot metadata does not match its request.",
                nameof(snapshot));
        }

        if (sampleCount <= 0 ||
            failedObservationCount < 0 ||
            hasChangedCount < 0 ||
            hasIncreasedCount < 0 ||
            hasDecreasedCount < 0 ||
            readFailedCandidateCount < 0 ||
            suppressedWarningCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount));
        }

        if (completedAt < startedAt)
        {
            throw new ArgumentException(
                "Completion cannot precede filter start.",
                nameof(completedAt));
        }

        Warnings = Array.AsReadOnly(
            warnings?.ToArray() ?? Array.Empty<Error>());

        if (Warnings.Any(error =>
            error is null || error.Code == ErrorCode.None))
        {
            throw new ArgumentException(
                "A warning must describe a failure.",
                nameof(warnings));
        }

        FilterId = filterId;
        SampleCount = sampleCount;
        FailedObservationCount = failedObservationCount;
        HasChangedCount = hasChangedCount;
        HasIncreasedCount = hasIncreasedCount;
        HasDecreasedCount = hasDecreasedCount;
        ReadFailedCandidateCount = readFailedCandidateCount;
        SuppressedWarningCount = suppressedWarningCount;
        StartedAt = startedAt;
        CompletedAt = completedAt;
    }

    public Guid FilterId { get; }

    public DurationFilterRequest Request { get; }

    public SnapshotDescriptor Snapshot { get; }

    public long MatchedCount => Snapshot.RecordCount;

    public long SampleCount { get; }

    public long FailedObservationCount { get; }

    public long HasChangedCount { get; }

    public long HasIncreasedCount { get; }

    public long HasDecreasedCount { get; }

    public long ReadFailedCandidateCount { get; }

    public IReadOnlyList<Error> Warnings { get; }

    public long SuppressedWarningCount { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletedAt { get; }

    public TimeSpan Elapsed => CompletedAt - StartedAt;

    public bool IsPartial => ReadFailedCandidateCount > 0;
}

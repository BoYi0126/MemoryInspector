using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning;

public sealed class NextScanResult
{
    public NextScanResult(
        Guid scanId,
        NextScanRequest request,
        SnapshotDescriptor snapshot,
        long examinedCount,
        long completeReadCount,
        long partialReadCount,
        long failedReadCount,
        IEnumerable<Error>? warnings,
        long suppressedWarningCount,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        if (scanId == Guid.Empty)
        {
            throw new ArgumentException(
                "Scan ID cannot be empty.",
                nameof(scanId));
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
                "Next snapshot metadata does not match its request.",
                nameof(snapshot));
        }

        if (examinedCount < 0 ||
            completeReadCount < 0 ||
            partialReadCount < 0 ||
            failedReadCount < 0 ||
            suppressedWarningCount < 0 ||
            completeReadCount +
                partialReadCount +
                failedReadCount != examinedCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(examinedCount));
        }

        if (snapshot.RecordCount > completeReadCount)
        {
            throw new ArgumentException(
                "Matched candidates cannot exceed complete reads.",
                nameof(snapshot));
        }

        if (completedAt < startedAt)
        {
            throw new ArgumentException(
                "Completion cannot precede scan start.",
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

        ScanId = scanId;
        ExaminedCount = examinedCount;
        CompleteReadCount = completeReadCount;
        PartialReadCount = partialReadCount;
        FailedReadCount = failedReadCount;
        SuppressedWarningCount = suppressedWarningCount;
        StartedAt = startedAt;
        CompletedAt = completedAt;
    }

    public Guid ScanId { get; }

    public NextScanRequest Request { get; }

    public SnapshotDescriptor Snapshot { get; }

    public long ExaminedCount { get; }

    public long MatchedCount => Snapshot.RecordCount;

    public long CompleteReadCount { get; }

    public long PartialReadCount { get; }

    public long FailedReadCount { get; }

    public IReadOnlyList<Error> Warnings { get; }

    public long SuppressedWarningCount { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletedAt { get; }

    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool IsPartial =>
        PartialReadCount > 0 ||
        FailedReadCount > 0;
}

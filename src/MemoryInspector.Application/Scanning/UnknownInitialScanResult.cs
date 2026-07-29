using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning;

public sealed class UnknownInitialScanResult
{
    public UnknownInitialScanResult(
        Guid scanId,
        UnknownInitialScanEstimate estimate,
        SnapshotDescriptor snapshot,
        long scannedBytes,
        IEnumerable<Error>? warnings,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        if (scanId == Guid.Empty)
        {
            throw new ArgumentException(
                "Scan ID cannot be empty.",
                nameof(scanId));
        }

        Estimate = estimate ??
            throw new ArgumentNullException(nameof(estimate));
        Snapshot = snapshot ??
            throw new ArgumentNullException(nameof(snapshot));

        if (snapshot.SessionId != estimate.SessionId ||
            snapshot.ValueType != estimate.ValueType ||
            !snapshot.IncludesValues)
        {
            throw new ArgumentException(
                "Snapshot metadata does not match the estimate.",
                nameof(snapshot));
        }

        if (scannedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scannedBytes));
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
                "A scan warning must describe a failure.",
                nameof(warnings));
        }

        ScanId = scanId;
        ScannedBytes = scannedBytes;
        StartedAt = startedAt;
        CompletedAt = completedAt;
    }

    public Guid ScanId { get; }

    public UnknownInitialScanEstimate Estimate { get; }

    public SnapshotDescriptor Snapshot { get; }

    public long CandidateCount => Snapshot.RecordCount;

    public long ScannedBytes { get; }

    public IReadOnlyList<Error> Warnings { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletedAt { get; }

    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool IsPartial => Warnings.Count > 0;

    public bool IsDiskBacked => true;
}

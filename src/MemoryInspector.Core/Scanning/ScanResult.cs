namespace MemoryInspector.Core.Scanning;

public sealed record ScanResult
{
    public ScanResult(
        Guid scanId,
        ScanRequest request,
        long scannedBytes,
        long candidateCount,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        bool isPartial)
    {
        if (scanId == Guid.Empty)
        {
            throw new ArgumentException(
                "Scan ID cannot be empty.",
                nameof(scanId));
        }

        Request = request ??
            throw new ArgumentNullException(nameof(request));

        if (scannedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scannedBytes));
        }

        if (candidateCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateCount));
        }

        if (completedAt < startedAt)
        {
            throw new ArgumentException(
                "Scan completion cannot precede its start.",
                nameof(completedAt));
        }

        ScanId = scanId;
        ScannedBytes = scannedBytes;
        CandidateCount = candidateCount;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        IsPartial = isPartial;
    }

    public Guid ScanId { get; }

    public ScanRequest Request { get; }

    public long ScannedBytes { get; }

    public long CandidateCount { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletedAt { get; }

    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool IsPartial { get; }
}

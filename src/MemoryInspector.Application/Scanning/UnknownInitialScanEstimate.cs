using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed record UnknownInitialScanEstimate
{
    public UnknownInitialScanEstimate(
        Guid sessionId,
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        long candidateCount,
        long scannableBytes,
        long estimatedDiskBytes,
        long memoryBudgetBytes,
        long snapshotThreshold,
        int scannableRegionCount,
        int skippedRegionCount)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Session ID cannot be empty.",
                nameof(sessionId));
        }

        _ = ScanValueTypeInfo.GetSize(valueType);

        if (!Enum.IsDefined(alignmentMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignmentMode));
        }

        if (candidateCount < 0 ||
            scannableBytes < 0 ||
            estimatedDiskBytes < 0 ||
            memoryBudgetBytes <= 0 ||
            snapshotThreshold <= 0 ||
            scannableRegionCount < 0 ||
            skippedRegionCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateCount));
        }

        SessionId = sessionId;
        ValueType = valueType;
        AlignmentMode = alignmentMode;
        CandidateCount = candidateCount;
        ScannableBytes = scannableBytes;
        EstimatedDiskBytes = estimatedDiskBytes;
        MemoryBudgetBytes = memoryBudgetBytes;
        SnapshotThreshold = snapshotThreshold;
        ScannableRegionCount = scannableRegionCount;
        SkippedRegionCount = skippedRegionCount;
    }

    public Guid SessionId { get; }

    public ScanValueType ValueType { get; }

    public ScanAlignmentMode AlignmentMode { get; }

    public long CandidateCount { get; }

    public long ScannableBytes { get; }

    public int ValueSize => ScanValueTypeInfo.GetSize(ValueType);

    public int RecordSize =>
        SnapshotFormatInfo.AddressSize + ValueSize;

    public long EstimatedDiskBytes { get; }

    public long MemoryBudgetBytes { get; }

    public long SnapshotThreshold { get; }

    public int ScannableRegionCount { get; }

    public int SkippedRegionCount { get; }

    public bool RequiresDiskBackedStorage =>
        CandidateCount >= SnapshotThreshold ||
        EstimatedDiskBytes > MemoryBudgetBytes;
}

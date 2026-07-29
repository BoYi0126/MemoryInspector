using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed record DurationFilterRequest
{
    public const int DefaultPageSize = 4096;
    public static readonly TimeSpan DefaultSampleInterval =
        TimeSpan.FromSeconds(1);

    public DurationFilterRequest(
        SnapshotDescriptor previousSnapshot,
        int targetNodeId,
        ScanRequest filter,
        TimeSpan duration,
        DurationFilterObservationMode observationMode,
        TimeSpan? sampleInterval = null,
        int pageSize = DefaultPageSize)
    {
        PreviousSnapshot = previousSnapshot ??
            throw new ArgumentNullException(
                nameof(previousSnapshot));
        Filter = filter ??
            throw new ArgumentNullException(nameof(filter));

        if (!previousSnapshot.IncludesValues)
        {
            throw new ArgumentException(
                "Duration Filter requires previous candidate values.",
                nameof(previousSnapshot));
        }

        if (previousSnapshot.ValueType != filter.ValueType ||
            previousSnapshot.ValueSize != filter.ValueSize)
        {
            throw new ArgumentException(
                "Filter type must match the previous snapshot.",
                nameof(filter));
        }

        if (!IsDurationMode(filter.ComparisonMode))
        {
            throw new ArgumentException(
                "Duration Filter supports Changed, Unchanged, " +
                "Increased, and Decreased comparisons.",
                nameof(filter));
        }

        if (targetNodeId <= 0 ||
            targetNodeId == previousSnapshot.NodeId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetNodeId),
                "Target node must be positive and different " +
                "from the previous node.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Duration must be greater than zero.");
        }

        if (!Enum.IsDefined(observationMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(observationMode));
        }

        var interval = sampleInterval ?? DefaultSampleInterval;

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleInterval),
                "Sample interval must be greater than zero.");
        }

        if (pageSize <= 0 ||
            pageSize > NextScanRequest.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                $"Page size must be between 1 and " +
                $"{NextScanRequest.MaximumPageSize:N0}.");
        }

        TargetNodeId = targetNodeId;
        Duration = duration;
        ObservationMode = observationMode;
        SampleInterval = interval;
        PageSize = pageSize;
    }

    public SnapshotDescriptor PreviousSnapshot { get; }

    public int TargetNodeId { get; }

    public ScanRequest Filter { get; }

    public TimeSpan Duration { get; }

    public DurationFilterObservationMode ObservationMode { get; }

    public TimeSpan SampleInterval { get; }

    public int PageSize { get; }

    private static bool IsDurationMode(ScanComparisonMode mode)
    {
        return mode is
            ScanComparisonMode.Changed or
            ScanComparisonMode.Unchanged or
            ScanComparisonMode.Increased or
            ScanComparisonMode.Decreased;
    }
}

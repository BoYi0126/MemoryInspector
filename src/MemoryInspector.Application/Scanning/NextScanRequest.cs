using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed record NextScanRequest
{
    public const int DefaultPageSize = 4096;
    public const int MaximumPageSize = 1_000_000;

    public NextScanRequest(
        SnapshotDescriptor previousSnapshot,
        int targetNodeId,
        ScanRequest filter,
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
                "Next Scan requires previous candidate values.",
                nameof(previousSnapshot));
        }

        if (previousSnapshot.ValueType != filter.ValueType ||
            previousSnapshot.ValueSize != filter.ValueSize)
        {
            throw new ArgumentException(
                "Filter type must match the previous snapshot.",
                nameof(filter));
        }

        if (filter.ComparisonMode ==
            ScanComparisonMode.UnknownInitialValue)
        {
            throw new ArgumentException(
                "Unknown Initial Value is not a Next Scan comparison.",
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

        if (pageSize <= 0 || pageSize > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                $"Page size must be between 1 and " +
                $"{MaximumPageSize:N0}.");
        }

        TargetNodeId = targetNodeId;
        PageSize = pageSize;
    }

    public SnapshotDescriptor PreviousSnapshot { get; }

    public int TargetNodeId { get; }

    public ScanRequest Filter { get; }

    public int PageSize { get; }
}

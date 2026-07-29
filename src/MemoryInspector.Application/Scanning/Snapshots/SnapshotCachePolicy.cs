using MemoryInspector.Application.Configuration;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Snapshots;

public sealed record SnapshotCachePolicy
{
    public const long DefaultMemoryBudgetBytes =
        512L * 1024 * 1024;
    public const int DefaultMaximumCachedNodes = 3;
    public const int DefaultPageSize = 1_000;
    public const int MaximumPageSize = 1_000_000;
    public const long DefaultMemoryOnlyThreshold = 100_000;
    public const long DefaultDiskBackedThreshold = 1_000_000;

    public SnapshotCachePolicy(
        long memoryBudgetBytes = DefaultMemoryBudgetBytes,
        int maximumCachedNodes = DefaultMaximumCachedNodes,
        int pageSize = DefaultPageSize,
        long memoryOnlyThreshold = DefaultMemoryOnlyThreshold,
        long diskBackedThreshold = DefaultDiskBackedThreshold)
    {
        MemoryBudgetBytes = memoryBudgetBytes;
        MaximumCachedNodes = maximumCachedNodes;
        PageSize = pageSize;
        MemoryOnlyThreshold = memoryOnlyThreshold;
        DiskBackedThreshold = diskBackedThreshold;
    }

    public long MemoryBudgetBytes { get; }

    public int MaximumCachedNodes { get; }

    public int PageSize { get; }

    public long MemoryOnlyThreshold { get; }

    public long DiskBackedThreshold { get; }

    public Result Validate()
    {
        if (MemoryBudgetBytes <= 0)
        {
            return Validation(
                "Memory budget must be greater than zero.");
        }

        if (MaximumCachedNodes <= 0)
        {
            return Validation(
                "Maximum cached nodes must be greater than zero.");
        }

        if (PageSize <= 0 ||
            PageSize > MaximumPageSize)
        {
            return Validation(
                $"Cache page size must be between 1 and " +
                $"{MaximumPageSize:N0}.");
        }

        if (MemoryOnlyThreshold <= 0 ||
            DiskBackedThreshold <= 0)
        {
            return Validation(
                "Snapshot cache thresholds must be greater than zero.");
        }

        if (MemoryOnlyThreshold > DiskBackedThreshold)
        {
            return Validation(
                "Memory-only threshold cannot exceed the " +
                "disk-backed threshold.");
        }

        return Result.Success();
    }

    public static SnapshotCachePolicy FromSettings(
        AppSettings settings)
    {
        Guard.NotNull(settings);

        return new SnapshotCachePolicy(
            settings.MemoryBudgetBytes,
            settings.CachedNodeCount,
            settings.PageSize,
            settings.MemoryOnlyThreshold,
            settings.SnapshotThreshold);
    }

    private static Result Validation(string message)
    {
        return Result.Failure(
            new Error(ErrorCode.Validation, message));
    }
}

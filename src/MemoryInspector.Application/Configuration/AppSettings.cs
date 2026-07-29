using MemoryInspector.Common;
using MemoryInspector.Application.Memory.Editing;

namespace MemoryInspector.Application.Configuration;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MinimumWatchRefreshIntervalMilliseconds = 50;
    public const int MaximumWatchRefreshIntervalMilliseconds = 60_000;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public long MemoryBudgetBytes { get; init; } = 512L * 1024 * 1024;

    public int CachedNodeCount { get; init; } = 3;

    public int PageSize { get; init; } = 1_000;

    public long MemoryOnlyThreshold { get; init; } = 100_000;

    public long SnapshotThreshold { get; init; } = 1_000_000;

    public int TempRetentionDays { get; init; } = 7;

    public int ProcessRefreshIntervalMilliseconds { get; init; } = 2_000;

    public int WatchRefreshIntervalMilliseconds { get; init; } = 500;

    public double DefaultNumericTolerance { get; init; } = 0.0001d;

    public MemoryEditorSettings MemoryEditor { get; init; } =
        MemoryEditorSettings.CreateDefault();

    public static AppSettings CreateDefault() => new();

    public Result Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            return ValidationFailure(
                $"Unsupported settings schema version {SchemaVersion}. " +
                $"Expected version {CurrentSchemaVersion}.");
        }

        if (MemoryBudgetBytes <= 0)
        {
            return ValidationFailure("Memory budget must be greater than zero.");
        }

        if (CachedNodeCount <= 0)
        {
            return ValidationFailure("Cached node count must be greater than zero.");
        }

        if (PageSize <= 0 || PageSize > 1_000_000)
        {
            return ValidationFailure(
                "Page size must be between 1 and 1,000,000.");
        }

        if (MemoryOnlyThreshold <= 0)
        {
            return ValidationFailure(
                "Memory-only threshold must be greater than zero.");
        }

        if (SnapshotThreshold <= 0)
        {
            return ValidationFailure("Snapshot threshold must be greater than zero.");
        }

        if (MemoryOnlyThreshold > SnapshotThreshold)
        {
            return ValidationFailure(
                "Memory-only threshold cannot exceed the snapshot threshold.");
        }

        if (TempRetentionDays < 0)
        {
            return ValidationFailure("Temp retention days cannot be negative.");
        }

        if (ProcessRefreshIntervalMilliseconds <= 0)
        {
            return ValidationFailure(
                "Process refresh interval must be greater than zero.");
        }

        if (WatchRefreshIntervalMilliseconds <
                MinimumWatchRefreshIntervalMilliseconds ||
            WatchRefreshIntervalMilliseconds >
                MaximumWatchRefreshIntervalMilliseconds)
        {
            return ValidationFailure(
                $"Watch refresh interval must be between " +
                $"{MinimumWatchRefreshIntervalMilliseconds} and " +
                $"{MaximumWatchRefreshIntervalMilliseconds} milliseconds.");
        }

        if (!double.IsFinite(DefaultNumericTolerance) ||
            DefaultNumericTolerance < 0)
        {
            return ValidationFailure(
                "Default numeric tolerance must be a finite, non-negative number.");
        }

        if (MemoryEditor is null)
        {
            return ValidationFailure(
                "Memory Editor settings are required.");
        }

        var memoryEditorValidation = MemoryEditor.Validate();

        if (memoryEditorValidation.IsFailure)
        {
            return memoryEditorValidation;
        }

        return Result.Success();
    }

    private static Result ValidationFailure(string message)
    {
        return Result.Failure(new Error(ErrorCode.Validation, message));
    }
}

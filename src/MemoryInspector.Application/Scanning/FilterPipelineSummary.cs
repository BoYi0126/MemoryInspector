using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed record FilterPipelineSummary
{
    public FilterPipelineSummary(
        FilterPipelineOperationKind operationKind,
        ScanComparisonMode comparisonMode,
        long beforeCount,
        long afterCount,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        bool isPartial,
        int warningCount,
        long suppressedWarningCount,
        TimeSpan? observationDuration = null,
        DurationFilterObservationMode? observationMode = null)
    {
        if (!Enum.IsDefined(operationKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationKind));
        }

        if (!Enum.IsDefined(comparisonMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(comparisonMode));
        }

        if (beforeCount < 0 ||
            afterCount < 0 ||
            afterCount > beforeCount ||
            warningCount < 0 ||
            suppressedWarningCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(beforeCount));
        }

        if (completedAt < startedAt)
        {
            throw new ArgumentException(
                "Completion cannot precede the filter start.",
                nameof(completedAt));
        }

        if (operationKind ==
            FilterPipelineOperationKind.DurationFilter)
        {
            if (observationDuration is null or { Ticks: <= 0 } ||
                observationMode is null ||
                !Enum.IsDefined(observationMode.Value))
            {
                throw new ArgumentException(
                    "A Duration Filter summary requires valid " +
                    "observation settings.",
                    nameof(observationDuration));
            }
        }
        else if (observationDuration is not null ||
                 observationMode is not null)
        {
            throw new ArgumentException(
                "Next Scan summaries cannot contain duration settings.",
                nameof(observationDuration));
        }

        OperationKind = operationKind;
        ComparisonMode = comparisonMode;
        BeforeCount = beforeCount;
        AfterCount = afterCount;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        IsPartial = isPartial;
        WarningCount = warningCount;
        SuppressedWarningCount = suppressedWarningCount;
        ObservationDuration = observationDuration;
        ObservationMode = observationMode;
    }

    public FilterPipelineOperationKind OperationKind { get; }

    public ScanComparisonMode ComparisonMode { get; }

    public long BeforeCount { get; }

    public long AfterCount { get; }

    public long RemovedCount => BeforeCount - AfterCount;

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletedAt { get; }

    public TimeSpan Elapsed => CompletedAt - StartedAt;

    public bool IsPartial { get; }

    public int WarningCount { get; }

    public long SuppressedWarningCount { get; }

    public TimeSpan? ObservationDuration { get; }

    public DurationFilterObservationMode? ObservationMode { get; }

    public string DisplayText =>
        $"{ComparisonMode}: {BeforeCount:N0} → {AfterCount:N0}";
}

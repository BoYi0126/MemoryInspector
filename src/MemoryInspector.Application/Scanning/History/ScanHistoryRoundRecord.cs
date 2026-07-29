using MemoryInspector.Core.Scanning;
using MemoryInspector.Application.Scanning.Snapshots;

namespace MemoryInspector.Application.Scanning.History;

public sealed record ScanHistoryRoundRecord
{
    public ScanHistoryRoundRecord(
        Guid roundId,
        Guid? parentRoundId,
        long roundNumber,
        string name,
        bool isPinned,
        FilterPipelineOperationKind? operationKind,
        ScanComparisonMode? comparisonMode,
        FilterPipelineInput? input,
        long beforeCount,
        long afterCount,
        DateTimeOffset createdAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        bool isPartial,
        int warningCount,
        long suppressedWarningCount,
        long? observationDurationTicks,
        DurationFilterObservationMode? observationMode,
        int snapshotNodeId,
        ScanValueType snapshotValueType,
        long snapshotRecordCount,
        string snapshotChecksum,
        string storageReference,
        SnapshotStorageKind snapshotStorageKind =
            SnapshotStorageKind.Full,
        int? snapshotParentNodeId = null,
        int snapshotChainDepth = 0,
        long snapshotAccumulatedDeltaBytes = 0)
    {
        if (roundId == Guid.Empty ||
            roundNumber < 0 ||
            string.IsNullOrWhiteSpace(name) ||
            name.Length > FilterPipelineRound.MaximumNameLength ||
            beforeCount < 0 ||
            afterCount < 0 ||
            afterCount > beforeCount ||
            warningCount < 0 ||
            suppressedWarningCount < 0 ||
            snapshotNodeId <= 0 ||
            snapshotRecordCount < 0)
        {
            throw new ArgumentException(
                "History round metadata is invalid.");
        }

        _ = ScanValueTypeInfo.GetSize(snapshotValueType);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            snapshotChecksum);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            storageReference);

        if (!Enum.IsDefined(snapshotStorageKind) ||
            snapshotChainDepth < 0 ||
            snapshotAccumulatedDeltaBytes < 0 ||
            (snapshotStorageKind == SnapshotStorageKind.Full &&
             (snapshotParentNodeId is not null ||
              snapshotChainDepth != 0 ||
              snapshotAccumulatedDeltaBytes != 0)) ||
            (snapshotStorageKind != SnapshotStorageKind.Full &&
             (snapshotParentNodeId is null or <= 0 ||
              snapshotChainDepth <= 0)))
        {
            throw new ArgumentException(
                "Snapshot storage metadata is invalid.");
        }

        if ((operationKind.HasValue &&
             !Enum.IsDefined(operationKind.Value)) ||
            (comparisonMode.HasValue &&
             !Enum.IsDefined(comparisonMode.Value)) ||
            (observationMode.HasValue &&
             !Enum.IsDefined(observationMode.Value)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationKind));
        }

        if (roundNumber == 0)
        {
            if (parentRoundId is not null ||
                operationKind is not null ||
                comparisonMode is not null ||
                input is not null ||
                startedAt is not null ||
                completedAt is not null ||
                beforeCount != afterCount ||
                afterCount != snapshotRecordCount)
            {
                throw new ArgumentException(
                    "Initial history round metadata is invalid.");
            }
        }
        else if (parentRoundId is null ||
                 parentRoundId == Guid.Empty ||
                 operationKind is null ||
                 comparisonMode is null ||
                 input is null ||
                 startedAt is null ||
                 completedAt is null ||
                 completedAt < startedAt ||
                 afterCount != snapshotRecordCount)
        {
            throw new ArgumentException(
                "Filtered history round metadata is incomplete.");
        }

        if (operationKind ==
            FilterPipelineOperationKind.DurationFilter)
        {
            if (observationDurationTicks is null or <= 0 ||
                observationMode is null)
            {
                throw new ArgumentException(
                    "Duration history metadata is incomplete.");
            }
        }
        else if (observationDurationTicks is not null ||
                 observationMode is not null)
        {
            throw new ArgumentException(
                "Next Scan history cannot include duration metadata.");
        }

        RoundId = roundId;
        ParentRoundId = parentRoundId;
        RoundNumber = roundNumber;
        Name = name.Trim();
        IsPinned = isPinned;
        OperationKind = operationKind;
        ComparisonMode = comparisonMode;
        Input = input;
        BeforeCount = beforeCount;
        AfterCount = afterCount;
        CreatedAt = createdAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        IsPartial = isPartial;
        WarningCount = warningCount;
        SuppressedWarningCount = suppressedWarningCount;
        ObservationDurationTicks = observationDurationTicks;
        ObservationMode = observationMode;
        SnapshotNodeId = snapshotNodeId;
        SnapshotValueType = snapshotValueType;
        SnapshotRecordCount = snapshotRecordCount;
        SnapshotChecksum = snapshotChecksum;
        StorageReference = storageReference;
        SnapshotStorageKind = snapshotStorageKind;
        SnapshotParentNodeId = snapshotParentNodeId;
        SnapshotChainDepth = snapshotChainDepth;
        SnapshotAccumulatedDeltaBytes =
            snapshotAccumulatedDeltaBytes;
    }

    public Guid RoundId { get; }

    public Guid? ParentRoundId { get; }

    public long RoundNumber { get; }

    public string Name { get; }

    public bool IsPinned { get; }

    public FilterPipelineOperationKind? OperationKind { get; }

    public ScanComparisonMode? ComparisonMode { get; }

    public FilterPipelineInput? Input { get; }

    public long BeforeCount { get; }

    public long AfterCount { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public bool IsPartial { get; }

    public int WarningCount { get; }

    public long SuppressedWarningCount { get; }

    public long? ObservationDurationTicks { get; }

    public DurationFilterObservationMode? ObservationMode { get; }

    public int SnapshotNodeId { get; }

    public ScanValueType SnapshotValueType { get; }

    public long SnapshotRecordCount { get; }

    public string SnapshotChecksum { get; }

    public string StorageReference { get; }

    public SnapshotStorageKind SnapshotStorageKind { get; }

    public int? SnapshotParentNodeId { get; }

    public int SnapshotChainDepth { get; }

    public long SnapshotAccumulatedDeltaBytes { get; }
}

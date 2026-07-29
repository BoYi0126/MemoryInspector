using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed record ScanTreeNode
{
    public ScanTreeNode(
        FilterPipelineRound round,
        IEnumerable<Guid> childNodeIds,
        bool isActive)
    {
        Round = round ??
            throw new ArgumentNullException(nameof(round));
        var children = childNodeIds?.ToArray() ??
            throw new ArgumentNullException(nameof(childNodeIds));

        if (children.Any(id => id == Guid.Empty) ||
            children.Distinct().Count() != children.Length)
        {
            throw new ArgumentException(
                "Child node IDs must be unique and non-empty.",
                nameof(childNodeIds));
        }

        ChildNodeIds = Array.AsReadOnly(children);
        IsActive = isActive;
    }

    public FilterPipelineRound Round { get; }

    public Guid NodeId => Round.RoundId;

    public Guid? ParentNodeId => Round.ParentRoundId;

    public IReadOnlyList<Guid> ChildNodeIds { get; }

    public ScanComparisonMode? FilterMode =>
        Round.Summary?.ComparisonMode;

    public long CandidateCount => Round.CandidateCount;

    public long? BeforeCount => Round.Summary?.BeforeCount;

    public long? AfterCount => Round.Summary?.AfterCount;

    public TimeSpan? Duration =>
        Round.Summary is null
            ? null
            : Round.Summary.ObservationDuration ??
              Round.Summary.Elapsed;

    public ScanTreeStorageType StorageType =>
        Round.Snapshot.StorageKind switch
        {
            Snapshots.SnapshotStorageKind.Full =>
                ScanTreeStorageType.FullSnapshot,
            Snapshots.SnapshotStorageKind.DeltaKeep =>
                ScanTreeStorageType.DeltaKeep,
            Snapshots.SnapshotStorageKind.DeltaRemove =>
                ScanTreeStorageType.DeltaRemove,
            _ => throw new InvalidOperationException(
                "Snapshot storage kind is invalid."),
        };

    public string StoragePath => Round.StorageReference;

    public int SnapshotNodeId => Round.Snapshot.NodeId;

    public bool IsPinned => Round.IsPinned;

    public bool IsActive { get; }
}

using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.Snapshots;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class SnapshotCompareNodeOption
{
    public SnapshotCompareNodeOption(FilterPipelineRound round)
    {
        Round = round ??
            throw new ArgumentNullException(nameof(round));
    }

    public FilterPipelineRound Round { get; }

    public Guid RoundId => Round.RoundId;

    public SnapshotDescriptor Snapshot => Round.Snapshot;

    public string DisplayName =>
        $"#{Round.RoundNumber:N0} {Round.Name} • " +
        $"{Round.CandidateCount:N0} records • " +
        $"{Round.Snapshot.StorageKind}";
}

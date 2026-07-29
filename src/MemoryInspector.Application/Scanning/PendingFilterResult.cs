namespace MemoryInspector.Application.Scanning;

public sealed record PendingFilterResult
{
    public PendingFilterResult(
        FilterPipelineRound parent,
        FilterPipelineRound round)
    {
        Parent = parent ??
            throw new ArgumentNullException(nameof(parent));
        Round = round ??
            throw new ArgumentNullException(nameof(round));

        if (round.ParentRoundId != parent.RoundId ||
            round.ParentRoundNumber != parent.RoundNumber ||
            round.Snapshot.SessionId != parent.Snapshot.SessionId ||
            round.Snapshot.ValueType != parent.Snapshot.ValueType)
        {
            throw new ArgumentException(
                "Pending result must be a compatible child " +
                "of its parent.",
                nameof(round));
        }
    }

    public FilterPipelineRound Parent { get; }

    public FilterPipelineRound Round { get; }

    public FilterPipelineSummary Summary => Round.Summary!;

    public long BeforeCount => Summary.BeforeCount;

    public long AfterCount => Summary.AfterCount;
}

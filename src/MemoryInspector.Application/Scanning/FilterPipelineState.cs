namespace MemoryInspector.Application.Scanning;

public sealed record FilterPipelineState
{
    public FilterPipelineState(
        FilterPipelineRound activeRound,
        PendingFilterResult? pendingResult,
        bool isFiltering,
        IEnumerable<FilterPipelineRound>? rounds = null)
    {
        ActiveRound = activeRound ??
            throw new ArgumentNullException(nameof(activeRound));

        if (pendingResult is not null &&
            pendingResult.Parent != activeRound)
        {
            throw new ArgumentException(
                "Pending result must belong to the active round.",
                nameof(pendingResult));
        }

        ActiveRound = activeRound;
        PendingResult = pendingResult;
        IsFiltering = isFiltering;
        var roundArray = rounds?.ToArray() ??
            [activeRound];

        if (roundArray.Length == 0 ||
            roundArray.All(round =>
                round.RoundId != activeRound.RoundId) ||
            (pendingResult is not null &&
             roundArray.All(round =>
                 round.RoundId !=
                 pendingResult.Round.RoundId)))
        {
            throw new ArgumentException(
                "Pipeline history does not contain its active state.",
                nameof(rounds));
        }

        Rounds = Array.AsReadOnly(roundArray);
        var nodes = roundArray
            .Select(round =>
                new ScanTreeNode(
                    round,
                    roundArray
                        .Where(candidate =>
                            candidate.ParentRoundId ==
                            round.RoundId)
                        .OrderBy(candidate =>
                            candidate.RoundNumber)
                        .Select(candidate =>
                            candidate.RoundId),
                    round.RoundId == activeRound.RoundId))
            .ToArray();
        TreeNodes = Array.AsReadOnly(nodes);
    }

    public FilterPipelineRound ActiveRound { get; }

    public PendingFilterResult? PendingResult { get; }

    public bool IsFiltering { get; }

    public IReadOnlyList<FilterPipelineRound> Rounds { get; }

    public IReadOnlyList<ScanTreeNode> TreeNodes { get; }

    public long CurrentCandidateCount =>
        PendingResult?.AfterCount ??
        ActiveRound.CandidateCount;

    public bool CanKeep =>
        !IsFiltering && PendingResult is not null;

    public bool CanDiscard => CanKeep;

    public bool CanContinueFiltering =>
        !IsFiltering && PendingResult is null;

    public bool CanUndo =>
        !IsFiltering &&
        PendingResult is null &&
        ActiveRound.ParentRoundId is not null &&
        TreeNodes.Single(node =>
            node.NodeId == ActiveRound.RoundId)
            .ChildNodeIds.Count == 0;

    public bool CanRedo =>
        !IsFiltering && PendingResult is not null;
}

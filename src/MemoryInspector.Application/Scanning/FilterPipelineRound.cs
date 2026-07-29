using MemoryInspector.Application.Scanning.Snapshots;

namespace MemoryInspector.Application.Scanning;

public sealed record FilterPipelineRound
{
    public const int MaximumNameLength = 200;

    public FilterPipelineRound(
        Guid roundId,
        Guid? parentRoundId,
        long roundNumber,
        long? parentRoundNumber,
        string name,
        SnapshotDescriptor snapshot,
        FilterPipelineSummary? summary,
        FilterPipelineInput? input,
        DateTimeOffset createdAt,
        bool isPinned = false)
    {
        if (roundId == Guid.Empty ||
            roundNumber < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roundNumber));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Length > MaximumNameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(name));
        }

        Snapshot = snapshot ??
            throw new ArgumentNullException(nameof(snapshot));

        if (roundNumber == 0)
        {
            if (parentRoundId is not null ||
                parentRoundNumber is not null ||
                summary is not null ||
                input is not null)
            {
                throw new ArgumentException(
                    "The initial pipeline round cannot have a " +
                    "parent or filter summary.");
            }
        }
        else if (parentRoundId is null ||
                 parentRoundId == Guid.Empty ||
                 parentRoundNumber is null ||
                 parentRoundNumber < 0 ||
                 parentRoundNumber >= roundNumber ||
                 summary is null ||
                 input is null ||
                 summary.AfterCount != snapshot.RecordCount)
        {
            throw new ArgumentException(
                "A filtered round requires a valid parent and summary.");
        }

        RoundId = roundId;
        ParentRoundId = parentRoundId;
        RoundNumber = roundNumber;
        ParentRoundNumber = parentRoundNumber;
        Name = name.Trim();
        Summary = summary;
        Input = input;
        CreatedAt = createdAt;
        IsPinned = isPinned;
    }

    public Guid RoundId { get; }

    public Guid? ParentRoundId { get; }

    public long RoundNumber { get; }

    public long? ParentRoundNumber { get; }

    public string Name { get; }

    public SnapshotDescriptor Snapshot { get; }

    public FilterPipelineSummary? Summary { get; }

    public FilterPipelineInput? Input { get; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsPinned { get; }

    public long CandidateCount => Snapshot.RecordCount;

    public string StorageReference => Snapshot.FilePath;

    public FilterPipelineRound Rename(string name)
    {
        return new FilterPipelineRound(
            RoundId,
            ParentRoundId,
            RoundNumber,
            ParentRoundNumber,
            name,
            Snapshot,
            Summary,
            Input,
            CreatedAt,
            IsPinned);
    }

    public FilterPipelineRound SetPinned(bool isPinned)
    {
        return new FilterPipelineRound(
            RoundId,
            ParentRoundId,
            RoundNumber,
            ParentRoundNumber,
            Name,
            Snapshot,
            Summary,
            Input,
            CreatedAt,
            isPinned);
    }
}

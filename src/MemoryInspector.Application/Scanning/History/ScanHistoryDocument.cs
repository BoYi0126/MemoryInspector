namespace MemoryInspector.Application.Scanning.History;

public sealed record ScanHistoryDocument
{
    public const int CurrentFormatVersion = 3;
    public const int MinimumSupportedFormatVersion = 1;

    public ScanHistoryDocument(
        int formatVersion,
        Guid sessionId,
        Guid activeRoundId,
        Guid? pendingRoundId,
        IReadOnlyList<ScanHistoryRoundRecord> rounds)
    {
        if (formatVersion < MinimumSupportedFormatVersion ||
            formatVersion > CurrentFormatVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(formatVersion));
        }

        if (sessionId == Guid.Empty ||
            activeRoundId == Guid.Empty)
        {
            throw new ArgumentException(
                "History identity cannot be empty.");
        }

        var roundArray = rounds?.ToArray() ??
            throw new ArgumentNullException(nameof(rounds));

        if (roundArray.Length == 0 ||
            roundArray.Select(round => round.RoundId)
                .Distinct()
                .Count() != roundArray.Length ||
            roundArray.All(round =>
                round.RoundId != activeRoundId) ||
            (pendingRoundId.HasValue &&
             roundArray.All(round =>
                 round.RoundId != pendingRoundId.Value)))
        {
            throw new ArgumentException(
                "History round references are inconsistent.",
                nameof(rounds));
        }

        FormatVersion = formatVersion;
        SessionId = sessionId;
        ActiveRoundId = activeRoundId;
        PendingRoundId = pendingRoundId;
        Rounds = Array.AsReadOnly(roundArray);
    }

    public int FormatVersion { get; }

    public Guid SessionId { get; }

    public Guid ActiveRoundId { get; }

    public Guid? PendingRoundId { get; }

    public IReadOnlyList<ScanHistoryRoundRecord> Rounds { get; }
}

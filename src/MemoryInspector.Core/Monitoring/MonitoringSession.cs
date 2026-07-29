namespace MemoryInspector.Core.Monitoring;

public sealed record MonitoringSession
{
    public required Guid SessionId { get; init; }

    public required MonitoringSessionIdentity Identity { get; init; }

    public required MonitoringSessionState State { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ConnectedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public string? StatusMessage { get; init; }

    public bool IsActive =>
        State is MonitoringSessionState.Connecting or
            MonitoringSessionState.Connected;
}

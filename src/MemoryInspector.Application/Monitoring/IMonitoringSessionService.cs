using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.Monitoring;

public interface IMonitoringSessionService : IAsyncDisposable
{
    MonitoringSession? CurrentSession { get; }

    event EventHandler<MonitoringSessionChangedEventArgs>? SessionChanged;

    Task<Result<MonitoringSession>> StartAsync(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken = default);

    Task<Result<MonitoringSession>> CheckLivenessAsync(
        CancellationToken cancellationToken = default);

    Task<Result> StopAsync(
        CancellationToken cancellationToken = default);
}

public sealed class MonitoringSessionChangedEventArgs(
    MonitoringSession session) : EventArgs
{
    public MonitoringSession Session { get; } =
        session ?? throw new ArgumentNullException(nameof(session));
}

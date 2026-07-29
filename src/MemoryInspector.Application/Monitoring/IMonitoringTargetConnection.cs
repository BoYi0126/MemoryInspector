using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.Monitoring;

public interface IMonitoringTargetConnection : IAsyncDisposable
{
    MonitoringSessionIdentity Identity { get; }

    Task<Result<bool>> IsAliveAsync(
        CancellationToken cancellationToken = default);
}

public interface IMonitoringTargetConnectionFactory
{
    Task<Result<IMonitoringTargetConnection>> ConnectAsync(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken = default);
}

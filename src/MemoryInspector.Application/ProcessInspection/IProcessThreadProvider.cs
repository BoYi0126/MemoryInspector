using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.ProcessInspection;

public interface IProcessThreadProvider
{
    Task<Result<ProcessThreadQueryResult>> GetThreadsAsync(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken = default);
}

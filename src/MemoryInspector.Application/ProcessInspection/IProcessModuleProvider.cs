using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.ProcessInspection;

public interface IProcessModuleProvider
{
    Task<Result<ProcessModuleQueryResult>> GetModulesAsync(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken = default);
}

using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.Memory;

public interface IMemoryRegionProvider
{
    Task<Result<MemoryRegionQueryResult>> GetRegionsAsync(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken = default);
}

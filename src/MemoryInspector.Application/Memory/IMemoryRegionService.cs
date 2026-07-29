using MemoryInspector.Common;

namespace MemoryInspector.Application.Memory;

public interface IMemoryRegionService
{
    Task<Result<MemoryRegionQueryResult>> GetRegionsAsync(
        CancellationToken cancellationToken = default);
}

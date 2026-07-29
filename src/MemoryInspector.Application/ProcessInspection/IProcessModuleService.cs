using MemoryInspector.Common;

namespace MemoryInspector.Application.ProcessInspection;

public interface IProcessModuleService
{
    Task<Result<ProcessModuleQueryResult>> GetModulesAsync(
        CancellationToken cancellationToken = default);
}

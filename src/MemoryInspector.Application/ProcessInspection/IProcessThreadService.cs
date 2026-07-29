using MemoryInspector.Common;

namespace MemoryInspector.Application.ProcessInspection;

public interface IProcessThreadService
{
    Task<Result<ProcessThreadQueryResult>> GetThreadsAsync(
        CancellationToken cancellationToken = default);
}

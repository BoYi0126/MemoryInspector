using MemoryInspector.Common;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.Application.Processes;

public interface ISystemProcessService
{
    Task<Result<IReadOnlyList<ProcessSummary>>> GetProcessesAsync(
        CancellationToken cancellationToken = default);
}

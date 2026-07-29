using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Application.Memory.Editing;

public interface IMemoryWriteService
{
    Task<MemoryWriteResult> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken = default);
}

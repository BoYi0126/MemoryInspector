using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning;

public interface IDurationFilterService
{
    Task<Result<DurationFilterResult>> FilterAsync(
        DurationFilterRequest request,
        DurationFilterExecutionControl? executionControl = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

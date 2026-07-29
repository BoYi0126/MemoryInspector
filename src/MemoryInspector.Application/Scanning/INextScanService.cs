using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning;

public interface INextScanService
{
    Task<Result<NextScanResult>> ScanAsync(
        NextScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

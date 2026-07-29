using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public interface IFirstScanService
{
    Task<Result<FirstScanResult>> ScanExactValueAsync(
        ScanRequest request,
        FirstScanOptions? options = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

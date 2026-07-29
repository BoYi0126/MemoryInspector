using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.Memory;

public interface IMemoryReaderProvider
{
    Task<Result<MemoryReadResult>> ReadAsync(
        MonitoringSessionIdentity identity,
        MemoryReadRequest request,
        MemoryReadOptions options,
        CancellationToken cancellationToken = default);

    Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
        MonitoringSessionIdentity identity,
        IReadOnlyList<MemoryReadRequest> requests,
        MemoryReadOptions options,
        CancellationToken cancellationToken = default);
}

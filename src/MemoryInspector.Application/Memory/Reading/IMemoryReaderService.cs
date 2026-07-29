using MemoryInspector.Common;

namespace MemoryInspector.Application.Memory;

public interface IMemoryReaderService
{
    Task<Result<MemoryReadResult>> ReadAsync(
        ulong address,
        int length,
        MemoryReadOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<Result<T>> TryReadAsync<T>(
        ulong address,
        MemoryReadOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : unmanaged;

    Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
        IEnumerable<MemoryReadRequest> requests,
        MemoryReadOptions? options = null,
        CancellationToken cancellationToken = default);
}

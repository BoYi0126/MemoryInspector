using MemoryInspector.Common;

namespace MemoryInspector.Application.Memory;

public sealed record MemoryBatchReadItem
{
    public MemoryBatchReadItem(
        MemoryReadRequest request,
        Result<MemoryReadResult> result)
    {
        Request = request ??
            throw new ArgumentNullException(nameof(request));
        Result = result ??
            throw new ArgumentNullException(nameof(result));
    }

    public MemoryReadRequest Request { get; }

    public Result<MemoryReadResult> Result { get; }
}

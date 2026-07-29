using MemoryInspector.Common;

namespace MemoryInspector.Application.Memory;

public sealed class MemoryReadResult
{
    private readonly byte[] _data;

    public MemoryReadResult(
        MemoryReadRequest request,
        ReadOnlySpan<byte> data,
        IEnumerable<Error>? warnings = null)
    {
        Request = request ??
            throw new ArgumentNullException(nameof(request));

        if (data.Length > request.Length)
        {
            throw new ArgumentException(
                "Read data cannot exceed the requested length.",
                nameof(data));
        }

        _data = data.ToArray();
        Warnings = Array.AsReadOnly(
            warnings?.ToArray() ?? Array.Empty<Error>());

        if (Warnings.Any(error =>
            error is null || error.Code == ErrorCode.None))
        {
            throw new ArgumentException(
                "A read warning must describe a failure.",
                nameof(warnings));
        }
    }

    public MemoryReadRequest Request { get; }

    public ReadOnlyMemory<byte> Data => _data;

    public int BytesRead => _data.Length;

    public IReadOnlyList<Error> Warnings { get; }

    public bool IsComplete =>
        BytesRead == Request.Length &&
        Warnings.Count == 0;

    public bool IsPartial => BytesRead > 0 && !IsComplete;
}

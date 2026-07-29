namespace MemoryInspector.Application.Memory;

public sealed record MemoryReadRequest
{
    public MemoryReadRequest(ulong address, int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                "Memory read length must be greater than zero.");
        }

        _ = checked(address + (ulong)length);
        Address = address;
        Length = length;
    }

    public ulong Address { get; }

    public int Length { get; }

    public ulong EndAddress => Address + (ulong)Length;
}

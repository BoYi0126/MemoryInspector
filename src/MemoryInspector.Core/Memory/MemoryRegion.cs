namespace MemoryInspector.Core.Memory;

public sealed record MemoryRegion
{
    private const MemoryProtection ReadableProtections =
        MemoryProtection.ReadOnly |
        MemoryProtection.ReadWrite |
        MemoryProtection.WriteCopy |
        MemoryProtection.ExecuteRead |
        MemoryProtection.ExecuteReadWrite |
        MemoryProtection.ExecuteWriteCopy;

    private const MemoryProtection WritableProtections =
        MemoryProtection.ReadWrite |
        MemoryProtection.WriteCopy |
        MemoryProtection.ExecuteReadWrite |
        MemoryProtection.ExecuteWriteCopy;

    private const MemoryProtection ExecutableProtections =
        MemoryProtection.Execute |
        MemoryProtection.ExecuteRead |
        MemoryProtection.ExecuteReadWrite |
        MemoryProtection.ExecuteWriteCopy;

    public MemoryRegion(
        ulong baseAddress,
        ulong size,
        ulong allocationBase,
        MemoryRegionState state,
        MemoryRegionType type,
        MemoryProtection protection)
    {
        if (size == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "A memory region must contain at least one byte.");
        }

        BaseAddress = baseAddress;
        Size = size;
        EndAddress = checked(baseAddress + size);
        AllocationBase = allocationBase;
        State = state;
        Type = type;
        Protection = protection;
    }

    public ulong BaseAddress { get; }

    /// <summary>
    /// Gets the first address after the region.
    /// </summary>
    public ulong EndAddress { get; }

    public ulong Size { get; }

    public ulong AllocationBase { get; }

    public MemoryRegionState State { get; }

    public MemoryRegionType Type { get; }

    public MemoryProtection Protection { get; }

    public bool IsGuard =>
        Protection.HasFlag(MemoryProtection.Guard);

    public bool IsNoAccess =>
        Protection.HasFlag(MemoryProtection.NoAccess);

    public bool IsReadable =>
        CanAccess(ReadableProtections);

    public bool IsWritable =>
        CanAccess(WritableProtections);

    public bool IsExecutable =>
        CanAccess(ExecutableProtections);

    private bool CanAccess(MemoryProtection allowed)
    {
        return State == MemoryRegionState.Committed &&
               !IsGuard &&
               !IsNoAccess &&
               (Protection & allowed) != MemoryProtection.None;
    }
}

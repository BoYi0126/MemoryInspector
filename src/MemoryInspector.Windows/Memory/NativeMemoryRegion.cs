namespace MemoryInspector.Windows.Memory;

internal readonly record struct NativeMemoryRegion(
    ulong BaseAddress,
    ulong AllocationBase,
    ulong RegionSize,
    uint State,
    uint Protection,
    uint Type);

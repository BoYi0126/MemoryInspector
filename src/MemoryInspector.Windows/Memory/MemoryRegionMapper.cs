using MemoryInspector.Core.Memory;

namespace MemoryInspector.Windows.Memory;

internal static class MemoryRegionMapper
{
    private const uint BaseProtectionMask = 0xFF;
    private const uint KnownProtectionMask =
        BaseProtectionMask |
        NativeMemoryConstants.PageGuard |
        NativeMemoryConstants.PageNoCache |
        NativeMemoryConstants.PageWriteCombine;

    public static MemoryRegion Map(NativeMemoryRegion native)
    {
        return new MemoryRegion(
            native.BaseAddress,
            native.RegionSize,
            native.AllocationBase,
            MapState(native.State),
            MapType(native.Type),
            MapProtection(native.Protection));
    }

    internal static MemoryRegionState MapState(uint state)
    {
        return state switch
        {
            NativeMemoryConstants.MemCommit =>
                MemoryRegionState.Committed,
            NativeMemoryConstants.MemReserve =>
                MemoryRegionState.Reserved,
            NativeMemoryConstants.MemFree =>
                MemoryRegionState.Free,
            _ => MemoryRegionState.Unknown,
        };
    }

    internal static MemoryRegionType MapType(uint type)
    {
        return type switch
        {
            0 => MemoryRegionType.None,
            NativeMemoryConstants.MemPrivate =>
                MemoryRegionType.Private,
            NativeMemoryConstants.MemMapped =>
                MemoryRegionType.Mapped,
            NativeMemoryConstants.MemImage =>
                MemoryRegionType.Image,
            _ => MemoryRegionType.Unknown,
        };
    }

    internal static MemoryProtection MapProtection(uint protection)
    {
        var result = (protection & BaseProtectionMask) switch
        {
            0 => MemoryProtection.None,
            NativeMemoryConstants.PageNoAccess =>
                MemoryProtection.NoAccess,
            NativeMemoryConstants.PageReadOnly =>
                MemoryProtection.ReadOnly,
            NativeMemoryConstants.PageReadWrite =>
                MemoryProtection.ReadWrite,
            NativeMemoryConstants.PageWriteCopy =>
                MemoryProtection.WriteCopy,
            NativeMemoryConstants.PageExecute =>
                MemoryProtection.Execute,
            NativeMemoryConstants.PageExecuteRead =>
                MemoryProtection.ExecuteRead,
            NativeMemoryConstants.PageExecuteReadWrite =>
                MemoryProtection.ExecuteReadWrite,
            NativeMemoryConstants.PageExecuteWriteCopy =>
                MemoryProtection.ExecuteWriteCopy,
            _ => MemoryProtection.Unknown,
        };

        if ((protection & NativeMemoryConstants.PageGuard) != 0)
        {
            result |= MemoryProtection.Guard;
        }

        if ((protection & NativeMemoryConstants.PageNoCache) != 0)
        {
            result |= MemoryProtection.NoCache;
        }

        if ((protection & NativeMemoryConstants.PageWriteCombine) != 0)
        {
            result |= MemoryProtection.WriteCombine;
        }

        if ((protection & ~KnownProtectionMask) != 0)
        {
            result |= MemoryProtection.Unknown;
        }

        return result;
    }
}

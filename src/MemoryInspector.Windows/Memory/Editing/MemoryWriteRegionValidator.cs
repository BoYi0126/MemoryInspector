using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Windows.Memory.Editing;

public sealed class MemoryWriteRegionValidator
{
    public MemoryWriteFailureReason Validate(
        MemoryRegion? region,
        ulong address,
        int byteCount)
    {
        if (byteCount <= 0)
        {
            return MemoryWriteFailureReason.InvalidAddress;
        }

        ulong endAddress;

        try
        {
            endAddress = checked(address + (ulong)byteCount);
        }
        catch (OverflowException)
        {
            return MemoryWriteFailureReason.RangeOverflow;
        }

        if (region is null)
        {
            return MemoryWriteFailureReason.RegionNotFound;
        }

        if (address < region.BaseAddress ||
            address >= region.EndAddress ||
            endAddress > region.EndAddress)
        {
            return MemoryWriteFailureReason.InvalidAddress;
        }

        if (region.State != MemoryRegionState.Committed)
        {
            return MemoryWriteFailureReason.RegionNotCommitted;
        }

        if (region.IsGuard)
        {
            return MemoryWriteFailureReason.GuardPage;
        }

        if (region.IsNoAccess || !region.IsWritable)
        {
            return MemoryWriteFailureReason.RegionNotWritable;
        }

        return MemoryWriteFailureReason.None;
    }
}

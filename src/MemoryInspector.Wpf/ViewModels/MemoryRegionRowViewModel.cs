using MemoryInspector.Common;
using MemoryInspector.Core.Memory;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class MemoryRegionRowViewModel
{
    public MemoryRegionRowViewModel(MemoryRegion region)
    {
        Region = region ?? throw new ArgumentNullException(nameof(region));
    }

    public MemoryRegion Region { get; }

    public bool IsStale => false;

    public ulong BaseAddress => Region.BaseAddress;

    public string BaseAddressDisplay => FormatAddress(BaseAddress);

    public ulong EndAddress => Region.EndAddress;

    public string EndAddressDisplay => FormatAddress(EndAddress);

    public ulong AllocationBase => Region.AllocationBase;

    public string AllocationBaseDisplay =>
        FormatAddress(AllocationBase);

    public ulong Size => Region.Size;

    public string SizeDisplay => Region.Size <= long.MaxValue
        ? ByteSizeFormatter.Format((long)Region.Size)
        : $"{Region.Size:N0} bytes";

    public MemoryRegionState State => Region.State;

    public MemoryRegionType Type => Region.Type;

    public MemoryProtection Protection => Region.Protection;

    public string ProtectionDisplay => Region.Protection.ToString();

    public bool IsReadable => Region.IsReadable;

    public bool IsWritable => Region.IsWritable;

    public bool IsExecutable => Region.IsExecutable;

    public bool IsGuard => Region.IsGuard;

    public string AccessDisplay =>
        $"{Flag(IsReadable, 'R')}" +
        $"{Flag(IsWritable, 'W')}" +
        $"{Flag(IsExecutable, 'X')}" +
        $"{Flag(IsGuard, 'G')}";

    public bool HasSameIdentity(MemoryRegionRowViewModel candidate)
    {
        return BaseAddress == candidate.BaseAddress &&
               EndAddress == candidate.EndAddress &&
               AllocationBase == candidate.AllocationBase;
    }

    private static string FormatAddress(ulong address)
    {
        return $"0x{address:X16}";
    }

    private static char Flag(bool enabled, char value)
    {
        return enabled ? value : '-';
    }
}

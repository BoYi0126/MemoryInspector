using MemoryInspector.Core.Memory;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class HexViewerRequestedEventArgs(
    ulong address,
    MemoryRegion? region = null) : EventArgs
{
    public ulong Address { get; } = address;

    public MemoryRegion? Region { get; } = region;
}

using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class MemoryEditRequestedEventArgs(
    ulong address,
    ScanValueType valueType,
    MemoryWriteSource source) : EventArgs
{
    public ulong Address { get; } = address;

    public ScanValueType ValueType { get; } = valueType;

    public MemoryWriteSource Source { get; } = source;
}

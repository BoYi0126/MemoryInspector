using MemoryInspector.Application.Scanning.Snapshots;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class ScanSnapshotReadyEventArgs(
    SnapshotDescriptor snapshot) : EventArgs
{
    public SnapshotDescriptor Snapshot { get; } =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));
}

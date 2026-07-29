namespace MemoryInspector.Application.Scanning.Snapshots;

public enum SnapshotStorageKind
{
    Full = 0,
    DeltaKeep = 1,
    DeltaRemove = 2,
}

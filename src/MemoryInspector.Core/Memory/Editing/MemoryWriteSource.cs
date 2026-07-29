namespace MemoryInspector.Core.Memory.Editing;

public enum MemoryWriteSource
{
    ScanResult = 0,
    WatchWindow = 1,
    SavedAddress = 2,
    ManualAddress = 3,
    HexViewer = 4,
}

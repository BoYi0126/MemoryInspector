namespace MemoryInspector.Core.Memory;

public enum MemoryRegionState
{
    Unknown = 0,
    Free = 1,
    Reserved = 2,
    Committed = 3,
}

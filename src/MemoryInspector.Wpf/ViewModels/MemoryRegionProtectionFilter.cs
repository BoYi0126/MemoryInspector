namespace MemoryInspector.Wpf.ViewModels;

public enum MemoryRegionProtectionFilter
{
    All = 0,
    NoAccess = 1,
    ReadOnly = 2,
    ReadWrite = 3,
    WriteCopy = 4,
    Execute = 5,
    ExecuteRead = 6,
    ExecuteReadWrite = 7,
    ExecuteWriteCopy = 8,
    Guard = 9,
    NoCache = 10,
    WriteCombine = 11,
    Unknown = 12,
}

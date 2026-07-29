namespace MemoryInspector.Core.Scanning;

public enum ScanComparisonMode
{
    ExactValue = 0,
    UnknownInitialValue = 1,
    Changed = 2,
    Unchanged = 3,
    Increased = 4,
    Decreased = 5,
    GreaterThan = 6,
    LessThan = 7,
}

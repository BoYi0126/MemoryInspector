namespace MemoryInspector.Core.Processes;

public enum ProcessAccessStatus
{
    Available = 0,
    Partial = 1,
    AccessDenied = 2,
    Exited = 3,
    Error = 4,
}

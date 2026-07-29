namespace MemoryInspector.Application.Watch;

public enum WatchReadStatus
{
    Pending = 0,
    Available = 1,
    Unreadable = 2,
    Paused = 3,
    TargetUnavailable = 4,
}

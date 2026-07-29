namespace MemoryInspector.Core.Monitoring;

public enum MonitoringSessionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    AccessDenied = 3,
    TargetExited = 4,
    Invalidated = 5,
    Error = 6,
}

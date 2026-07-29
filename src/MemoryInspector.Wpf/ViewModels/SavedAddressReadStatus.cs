namespace MemoryInspector.Wpf.ViewModels;

public enum SavedAddressReadStatus
{
    Unverified = 0,
    Available = 1,
    Unreadable = 2,
    TargetMismatch = 3,
    TargetUnavailable = 4,
}

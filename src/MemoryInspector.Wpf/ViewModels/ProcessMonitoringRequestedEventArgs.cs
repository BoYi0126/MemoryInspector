namespace MemoryInspector.Wpf.ViewModels;

public sealed class ProcessMonitoringRequestedEventArgs(
    ProcessRowViewModel process) : EventArgs
{
    public ProcessRowViewModel Process { get; } =
        process ?? throw new ArgumentNullException(nameof(process));
}

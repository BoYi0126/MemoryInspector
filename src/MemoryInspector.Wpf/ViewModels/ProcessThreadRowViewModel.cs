using MemoryInspector.Core.ProcessInspection;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class ProcessThreadRowViewModel(
    ProcessThreadInfo thread)
{
    public ProcessThreadInfo Thread { get; } =
        thread ?? throw new ArgumentNullException(nameof(thread));

    public bool IsStale => false;

    public int ThreadId => Thread.ThreadId;

    public string StateDisplay =>
        Thread.State ?? "Unavailable";

    public int? Priority => Thread.Priority;

    public string PriorityDisplay =>
        Priority?.ToString() ?? "Unavailable";

    public DateTimeOffset? StartTime => Thread.StartTime;

    public string StartTimeDisplay =>
        StartTime?.ToLocalTime().ToString("G") ??
        "Unavailable";

    public TimeSpan? CpuTime => Thread.CpuTime;

    public string CpuTimeDisplay =>
        CpuTime?.ToString("c") ?? "Unavailable";

    public string WarningDisplay => string.Join(
        " | ",
        Thread.Warnings.Select(warning => warning.Message));
}

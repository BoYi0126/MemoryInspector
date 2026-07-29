using MemoryInspector.Common;

namespace MemoryInspector.Core.ProcessInspection;

public sealed record ProcessThreadInfo
{
    public ProcessThreadInfo(
        int threadId,
        string? state,
        int? priority,
        DateTimeOffset? startTime,
        TimeSpan? cpuTime,
        IReadOnlyList<Error>? warnings = null)
    {
        if (threadId < 0 ||
            priority is < 0 ||
            (cpuTime.HasValue &&
             cpuTime.Value < TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(
                nameof(threadId));
        }

        ThreadId = threadId;
        State = string.IsNullOrWhiteSpace(state)
            ? null
            : state.Trim();
        Priority = priority;
        StartTime = startTime;
        CpuTime = cpuTime;
        Warnings = Array.AsReadOnly(
            warnings?.ToArray() ?? []);
    }

    public int ThreadId { get; }

    public string? State { get; }

    public int? Priority { get; }

    public DateTimeOffset? StartTime { get; }

    public TimeSpan? CpuTime { get; }

    public IReadOnlyList<Error> Warnings { get; }

    public bool IsPartial => Warnings.Count > 0;
}

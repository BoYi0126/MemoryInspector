using MemoryInspector.Common;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.Core.Monitoring;

public sealed record MonitoringSessionIdentity
{
    public MonitoringSessionIdentity(
        int processId,
        DateTimeOffset processStartTime,
        ProcessArchitecture architecture,
        string processName)
    {
        if (processId < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId),
                processId,
                "Process ID cannot be negative.");
        }

        if (architecture == ProcessArchitecture.Unknown)
        {
            throw new ArgumentException(
                "A monitoring session requires a known process architecture.",
                nameof(architecture));
        }

        ProcessId = processId;
        ProcessStartTime = processStartTime;
        Architecture = architecture;
        ProcessName = Guard.NotNullOrWhiteSpace(processName);
    }

    public int ProcessId { get; }

    public DateTimeOffset ProcessStartTime { get; }

    public ProcessArchitecture Architecture { get; }

    public string ProcessName { get; }
}

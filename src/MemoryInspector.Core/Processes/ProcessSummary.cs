namespace MemoryInspector.Core.Processes;

public sealed record ProcessSummary
{
    public required string ProcessName { get; init; }

    public required int ProcessId { get; init; }

    public double? CpuUsagePercentage { get; init; }

    public long? WorkingSetBytes { get; init; }

    public long? PrivateMemoryBytes { get; init; }

    public long? VirtualMemoryBytes { get; init; }

    public ProcessArchitecture Architecture { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    public string? ExecutablePath { get; init; }

    public ProcessAccessStatus AccessStatus { get; init; }

    public string? StatusMessage { get; init; }
}

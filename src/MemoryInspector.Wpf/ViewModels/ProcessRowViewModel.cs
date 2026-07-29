using System.Globalization;
using MemoryInspector.Common;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class ProcessRowViewModel
{
    public ProcessRowViewModel(
        ProcessSummary summary,
        bool isStale = false)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        IsStale = isStale;
    }

    public ProcessSummary Summary { get; }

    public bool IsStale { get; }

    public string ProcessName => Summary.ProcessName;

    public int ProcessId => Summary.ProcessId;

    public double? CpuUsagePercentage => Summary.CpuUsagePercentage;

    public string CpuDisplay => CpuUsagePercentage.HasValue
        ? string.Create(
            CultureInfo.CurrentCulture,
            $"{CpuUsagePercentage.Value:0.0}%")
        : "—";

    public long? WorkingSetBytes => Summary.WorkingSetBytes;

    public string WorkingSetDisplay => FormatBytes(Summary.WorkingSetBytes);

    public long? PrivateMemoryBytes => Summary.PrivateMemoryBytes;

    public string PrivateMemoryDisplay => FormatBytes(Summary.PrivateMemoryBytes);

    public string VirtualMemoryDisplay => FormatBytes(Summary.VirtualMemoryBytes);

    public ProcessArchitecture Architecture => Summary.Architecture;

    public string ArchitectureDisplay => Summary.Architecture == ProcessArchitecture.Unknown
        ? "Unknown"
        : Summary.Architecture.ToString();

    public DateTimeOffset? StartTime => Summary.StartTime;

    public string StartTimeDisplay => Summary.StartTime.HasValue
        ? Summary.StartTime.Value
            .ToLocalTime()
            .ToString(
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.CurrentCulture)
        : "Unavailable";

    public string ExecutablePathDisplay =>
        string.IsNullOrWhiteSpace(Summary.ExecutablePath)
            ? "Unavailable"
            : Summary.ExecutablePath;

    public ProcessAccessStatus AccessStatus => Summary.AccessStatus;

    public string StatusDisplay => IsStale
        ? "Exited"
        : Summary.AccessStatus.ToString();

    public string StatusMessage =>
        Summary.StatusMessage ??
        (Summary.AccessStatus == ProcessAccessStatus.Available
            ? "Process information is available."
            : "Some process information is unavailable.");

    public bool CanStartMonitoring =>
        !IsStale &&
        Summary.StartTime.HasValue &&
        Summary.Architecture != ProcessArchitecture.Unknown &&
        Summary.AccessStatus is
            ProcessAccessStatus.Available or
            ProcessAccessStatus.Partial;

    public bool HasSameIdentity(ProcessSummary candidate)
    {
        return Summary.StartTime.HasValue &&
               candidate.StartTime.HasValue &&
               Summary.ProcessId == candidate.ProcessId &&
               Summary.StartTime.Value.ToUniversalTime() ==
               candidate.StartTime.Value.ToUniversalTime();
    }

    public ProcessRowViewModel MarkExited()
    {
        return new ProcessRowViewModel(
            Summary with
            {
                AccessStatus = ProcessAccessStatus.Exited,
                StatusMessage =
                    "Process is no longer present in the latest refresh.",
            },
            isStale: true);
    }

    private static string FormatBytes(long? value)
    {
        return value.HasValue
            ? ByteSizeFormatter.Format(value.Value)
            : "—";
    }
}

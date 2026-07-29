using System.Diagnostics;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.Windows.Processes;

internal sealed class SystemProcessSource : IProcessSource
{
    public IReadOnlyList<IProcessAccessor> GetProcesses()
    {
        return Process
            .GetProcesses()
            .Select(process => (IProcessAccessor)new SystemProcessAccessor(process))
            .ToArray();
    }
}

internal sealed class SystemProcessAccessor(Process process) : IProcessAccessor
{
    private readonly Process _process = process;

    public int ProcessId => _process.Id;

    public string ProcessName => _process.ProcessName;

    public bool HasExited => _process.HasExited;

    public TimeSpan TotalProcessorTime => _process.TotalProcessorTime;

    public long WorkingSetBytes => _process.WorkingSet64;

    public long PrivateMemoryBytes => _process.PrivateMemorySize64;

    public long VirtualMemoryBytes => _process.VirtualMemorySize64;

    public DateTime StartTime => _process.StartTime;

    public string? ExecutablePath => _process.MainModule?.FileName;

    public ProcessArchitecture GetArchitecture()
    {
        return ProcessArchitectureDetector.Detect(ProcessId);
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}

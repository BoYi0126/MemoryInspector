using MemoryInspector.Core.Processes;

namespace MemoryInspector.Windows.Processes;

internal interface IProcessAccessor : IDisposable
{
    int ProcessId { get; }

    string ProcessName { get; }

    bool HasExited { get; }

    TimeSpan TotalProcessorTime { get; }

    long WorkingSetBytes { get; }

    long PrivateMemoryBytes { get; }

    long VirtualMemoryBytes { get; }

    DateTime StartTime { get; }

    string? ExecutablePath { get; }

    ProcessArchitecture GetArchitecture();
}

internal interface IProcessSource
{
    IReadOnlyList<IProcessAccessor> GetProcesses();
}

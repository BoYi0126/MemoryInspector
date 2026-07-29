using MemoryInspector.Core.Processes;
using MemoryInspector.Windows.Processes;

namespace MemoryInspector.Windows.Tests.Processes;

internal sealed class FakeProcessSource(
    Func<IReadOnlyList<IProcessAccessor>> getProcesses) : IProcessSource
{
    private readonly Func<IReadOnlyList<IProcessAccessor>> _getProcesses =
        getProcesses;

    public int CallCount { get; private set; }

    public IReadOnlyList<IProcessAccessor> GetProcesses()
    {
        CallCount++;
        return _getProcesses();
    }
}

internal sealed class FakeProcessAccessor : IProcessAccessor
{
    public int ProcessId { get; init; } = 100;

    public Func<string> ProcessNameFactory { get; init; } = () => "TestProcess";

    public Func<bool> HasExitedFactory { get; init; } = () => false;

    public Func<TimeSpan> TotalProcessorTimeFactory { get; init; } =
        () => TimeSpan.Zero;

    public Func<long> WorkingSetBytesFactory { get; init; } = () => 1_024;

    public Func<long> PrivateMemoryBytesFactory { get; init; } = () => 2_048;

    public Func<long> VirtualMemoryBytesFactory { get; init; } = () => 4_096;

    public Func<DateTime> StartTimeFactory { get; init; } =
        () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public Func<string?> ExecutablePathFactory { get; init; } =
        () => @"C:\Test\TestProcess.exe";

    public Func<ProcessArchitecture> ArchitectureFactory { get; init; } =
        () => ProcessArchitecture.X64;

    public bool IsDisposed { get; private set; }

    public string ProcessName => ProcessNameFactory();

    public bool HasExited => HasExitedFactory();

    public TimeSpan TotalProcessorTime => TotalProcessorTimeFactory();

    public long WorkingSetBytes => WorkingSetBytesFactory();

    public long PrivateMemoryBytes => PrivateMemoryBytesFactory();

    public long VirtualMemoryBytes => VirtualMemoryBytesFactory();

    public DateTime StartTime => StartTimeFactory();

    public string? ExecutablePath => ExecutablePathFactory();

    public ProcessArchitecture GetArchitecture() => ArchitectureFactory();

    public void Dispose()
    {
        IsDisposed = true;
    }
}

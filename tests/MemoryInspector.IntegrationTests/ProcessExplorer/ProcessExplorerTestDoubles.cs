using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Processes;
using MemoryInspector.Common;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.IntegrationTests.ProcessExplorer;

internal sealed class DelegateProcessService(
    Func<CancellationToken, Task<Result<IReadOnlyList<ProcessSummary>>>> getProcesses)
    : ISystemProcessService
{
    private readonly Func<
        CancellationToken,
        Task<Result<IReadOnlyList<ProcessSummary>>>> _getProcesses =
        getProcesses;

    public int CallCount => Volatile.Read(ref _callCount);

    private int _callCount;

    public Task<Result<IReadOnlyList<ProcessSummary>>> GetProcessesAsync(
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return _getProcesses(cancellationToken);
    }
}

internal sealed class QueueProcessService(
    params IReadOnlyList<ProcessSummary>[] responses) : ISystemProcessService
{
    private readonly Queue<IReadOnlyList<ProcessSummary>> _responses =
        new(responses);
    private readonly object _sync = new();

    public Task<Result<IReadOnlyList<ProcessSummary>>> GetProcessesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var response = _responses.Count > 1
                ? _responses.Dequeue()
                : _responses.Peek();
            return Task.FromResult(
                Result<IReadOnlyList<ProcessSummary>>.Success(response));
        }
    }
}

internal sealed class TestLogger : IAppLogger
{
    public List<(AppLogLevel Level, string Message, Exception? Exception)> Entries
    {
        get;
    } = [];

    public Result Log(
        AppLogLevel level,
        string message,
        Exception? exception = null)
    {
        lock (Entries)
        {
            Entries.Add((level, message, exception));
        }

        return Result.Success();
    }
}

internal static class ProcessSummaryFactory
{
    public static ProcessSummary Create(
        int processId,
        string name,
        DateTimeOffset? startTime = null,
        long? workingSet = 1_024,
        long? privateMemory = 2_048,
        double? cpu = 1d,
        ProcessAccessStatus status = ProcessAccessStatus.Available)
    {
        return new ProcessSummary
        {
            ProcessId = processId,
            ProcessName = name,
            StartTime = startTime ??
                new DateTimeOffset(
                    2026,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero).AddSeconds(processId),
            WorkingSetBytes = workingSet,
            PrivateMemoryBytes = privateMemory,
            VirtualMemoryBytes = 4_096,
            CpuUsagePercentage = cpu,
            Architecture = ProcessArchitecture.X64,
            ExecutablePath = $@"C:\Processes\{name}.exe",
            AccessStatus = status,
        };
    }
}

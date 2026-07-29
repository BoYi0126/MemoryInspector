using System.ComponentModel;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Processes;
using MemoryInspector.Common;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.Windows.Processes;

public sealed class SystemProcessService : ISystemProcessService, IDisposable
{
    private readonly SemaphoreSlim _enumerationGate = new(1, 1);
    private readonly IProcessSource _processSource;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly int _processorCount;
    private Dictionary<ProcessIdentity, CpuSample> _cpuSamples = [];
    private bool _disposed;

    public SystemProcessService(IAppLogger logger)
        : this(
            new SystemProcessSource(),
            logger,
            TimeProvider.System,
            Environment.ProcessorCount)
    {
    }

    internal SystemProcessService(
        IProcessSource processSource,
        IAppLogger logger,
        TimeProvider timeProvider,
        int processorCount)
    {
        _processSource = Guard.NotNull(processSource);
        _logger = Guard.NotNull(logger);
        _timeProvider = Guard.NotNull(timeProvider);
        _processorCount = Guard.Positive(processorCount);
    }

    public async Task<Result<IReadOnlyList<ProcessSummary>>> GetProcessesAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _enumerationGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return CancelledResult(exception);
        }

        IReadOnlyList<IProcessAccessor>? processes = null;
        var nextProcessIndex = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            processes = _processSource.GetProcesses();

            var sampleTime = _timeProvider.GetUtcNow();
            var nextCpuSamples = new Dictionary<ProcessIdentity, CpuSample>();
            var summaries = new List<ProcessSummary>(processes.Count);

            while (nextProcessIndex < processes.Count)
            {
                var process = processes[nextProcessIndex];

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    summaries.Add(
                        CaptureProcess(process, sampleTime, nextCpuSamples));
                }
                finally
                {
                    nextProcessIndex++;
                    process.Dispose();
                }
            }

            _cpuSamples = nextCpuSamples;

            IReadOnlyList<ProcessSummary> ordered = summaries
                .OrderBy(summary => summary.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(summary => summary.ProcessId)
                .ToArray();

            return Result<IReadOnlyList<ProcessSummary>>.Success(ordered);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return CancelledResult(exception);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            Win32Exception or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            _ = _logger.Log(
                AppLogLevel.Error,
                "The system process list could not be enumerated.",
                exception);

            return Result<IReadOnlyList<ProcessSummary>>.Failure(
                new Error(
                    ErrorCode.NativeApi,
                    "The system process list could not be enumerated.",
                    exception));
        }
        finally
        {
            if (processes is not null)
            {
                for (var index = nextProcessIndex; index < processes.Count; index++)
                {
                    processes[index].Dispose();
                }
            }

            _enumerationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _enumerationGate.Dispose();
        _disposed = true;
    }

    private ProcessSummary CaptureProcess(
        IProcessAccessor process,
        DateTimeOffset sampleTime,
        Dictionary<ProcessIdentity, CpuSample> nextCpuSamples)
    {
        var state = new CaptureState(_logger, process.ProcessId);

        var exited = ReadField(
            process,
            nameof(process.HasExited),
            () => process.HasExited,
            state);

        if (exited.IsSuccess && exited.Value)
        {
            state.MarkExited();

            return CreateMinimalSummary(process, state);
        }

        var name = ReadField(
            process,
            nameof(process.ProcessName),
            () => process.ProcessName,
            state);
        var startTime = ReadField(
            process,
            nameof(process.StartTime),
            () => process.StartTime,
            state);
        var totalProcessorTime = ReadField(
            process,
            nameof(process.TotalProcessorTime),
            () => process.TotalProcessorTime,
            state);
        var workingSet = ReadField(
            process,
            nameof(process.WorkingSetBytes),
            () => process.WorkingSetBytes,
            state);
        var privateMemory = ReadField(
            process,
            nameof(process.PrivateMemoryBytes),
            () => process.PrivateMemoryBytes,
            state);
        var virtualMemory = ReadField(
            process,
            nameof(process.VirtualMemoryBytes),
            () => process.VirtualMemoryBytes,
            state);
        var architecture = ReadField(
            process,
            "Architecture",
            process.GetArchitecture,
            state);
        var executablePath = ReadField(
            process,
            nameof(process.ExecutablePath),
            () => process.ExecutablePath,
            state);

        DateTimeOffset? processStartTime = startTime.IsSuccess
            ? new DateTimeOffset(startTime.Value)
            : null;
        double? cpuUsage = null;

        if (processStartTime.HasValue && totalProcessorTime.IsSuccess)
        {
            var identity = new ProcessIdentity(
                process.ProcessId,
                processStartTime.Value.UtcTicks);
            var currentSample = new CpuSample(
                totalProcessorTime.Value,
                sampleTime);

            if (_cpuSamples.TryGetValue(identity, out var previousSample))
            {
                cpuUsage = CalculateCpuUsage(previousSample, currentSample);
            }

            nextCpuSamples[identity] = currentSample;
        }

        return new ProcessSummary
        {
            ProcessName = name.IsSuccess && !string.IsNullOrWhiteSpace(name.Value)
                ? name.Value
                : $"Process {process.ProcessId}",
            ProcessId = process.ProcessId,
            CpuUsagePercentage = cpuUsage,
            WorkingSetBytes = workingSet.IsSuccess ? workingSet.Value : null,
            PrivateMemoryBytes = privateMemory.IsSuccess
                ? privateMemory.Value
                : null,
            VirtualMemoryBytes = virtualMemory.IsSuccess
                ? virtualMemory.Value
                : null,
            Architecture = architecture.IsSuccess
                ? architecture.Value
                : ProcessArchitecture.Unknown,
            StartTime = processStartTime,
            ExecutablePath = executablePath.IsSuccess
                ? executablePath.Value
                : null,
            AccessStatus = state.Status,
            StatusMessage = state.StatusMessage,
        };
    }

    private static ProcessSummary CreateMinimalSummary(
        IProcessAccessor process,
        CaptureState state)
    {
        return new ProcessSummary
        {
            ProcessName = $"Process {process.ProcessId}",
            ProcessId = process.ProcessId,
            Architecture = ProcessArchitecture.Unknown,
            AccessStatus = state.Status,
            StatusMessage = state.StatusMessage,
        };
    }

    private static Result<T> ReadField<T>(
        IProcessAccessor process,
        string fieldName,
        Func<T> read,
        CaptureState state)
    {
        try
        {
            return Result<T>.Success(read());
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == 5)
        {
            var error = new Error(
                ErrorCode.AccessDenied,
                $"Access to process field '{fieldName}' was denied.",
                exception);
            state.MarkFailure(error);
            return Result<T>.Failure(error);
        }
        catch (InvalidOperationException exception)
        {
            var error = new Error(
                ErrorCode.NotFound,
                $"Process {process.ProcessId} exited while '{fieldName}' was read.",
                exception);
            state.MarkFailure(error);
            return Result<T>.Failure(error);
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            NotSupportedException or
            PlatformNotSupportedException)
        {
            var error = new Error(
                ErrorCode.NativeApi,
                $"Process field '{fieldName}' could not be read.",
                exception);
            state.MarkFailure(error);
            return Result<T>.Failure(error);
        }
    }

    private double? CalculateCpuUsage(
        CpuSample previousSample,
        CpuSample currentSample)
    {
        var elapsed = currentSample.CapturedAt - previousSample.CapturedAt;
        var processorTime =
            currentSample.TotalProcessorTime - previousSample.TotalProcessorTime;

        if (elapsed <= TimeSpan.Zero || processorTime < TimeSpan.Zero)
        {
            return null;
        }

        var percentage =
            processorTime.TotalMilliseconds /
            (elapsed.TotalMilliseconds * _processorCount) *
            100d;

        return Math.Clamp(percentage, 0d, 100d);
    }

    private static Result<IReadOnlyList<ProcessSummary>> CancelledResult(
        OperationCanceledException exception)
    {
        return Result<IReadOnlyList<ProcessSummary>>.Failure(
            new Error(
                ErrorCode.Cancelled,
                "Process enumeration was cancelled.",
                exception));
    }

    private readonly record struct ProcessIdentity(
        int ProcessId,
        long StartTimeUtcTicks);

    private readonly record struct CpuSample(
        TimeSpan TotalProcessorTime,
        DateTimeOffset CapturedAt);

    private sealed class CaptureState(
        IAppLogger logger,
        int processId)
    {
        private bool _accessDenied;
        private bool _exited;
        private bool _unexpectedFailure;

        public ProcessAccessStatus Status => (_exited, _accessDenied, _unexpectedFailure) switch
        {
            (true, _, _) => ProcessAccessStatus.Exited,
            (_, true, _) => ProcessAccessStatus.AccessDenied,
            (_, _, true) => ProcessAccessStatus.Partial,
            _ => ProcessAccessStatus.Available,
        };

        public string? StatusMessage => Status switch
        {
            ProcessAccessStatus.Exited => "Process exited during enumeration.",
            ProcessAccessStatus.AccessDenied =>
                "Access to one or more process fields was denied.",
            ProcessAccessStatus.Partial =>
                "Some process information could not be read.",
            _ => null,
        };

        public void MarkExited()
        {
            _exited = true;
        }

        public void MarkFailure(Error error)
        {
            switch (error.Code)
            {
                case ErrorCode.AccessDenied:
                    _accessDenied = true;
                    break;
                case ErrorCode.NotFound:
                    _exited = true;
                    break;
                default:
                    _unexpectedFailure = true;
                    _ = logger.Log(
                        AppLogLevel.Warning,
                        $"Process {processId}: {error.ToDisplayMessage()}",
                        error.Exception);
                    break;
            }
        }
    }
}

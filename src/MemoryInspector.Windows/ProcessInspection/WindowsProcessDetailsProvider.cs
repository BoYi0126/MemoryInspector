using System.ComponentModel;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.ProcessInspection;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.ProcessInspection;
using MemoryInspector.Windows.Memory;

namespace MemoryInspector.Windows.ProcessInspection;

public sealed class WindowsProcessDetailsProvider :
    IProcessModuleProvider,
    IProcessThreadProvider
{
    private readonly IWindowsProcessDetailsSource _source;
    private readonly IProcessIdentityValidator _identityValidator;
    private readonly IAppLogger _logger;

    public WindowsProcessDetailsProvider(IAppLogger logger)
        : this(
            new SystemProcessDetailsSource(),
            new WindowsProcessIdentityValidator(),
            logger)
    {
    }

    internal WindowsProcessDetailsProvider(
        IWindowsProcessDetailsSource source,
        IProcessIdentityValidator identityValidator,
        IAppLogger logger)
    {
        _source = source ??
            throw new ArgumentNullException(nameof(source));
        _identityValidator = identityValidator ??
            throw new ArgumentNullException(
                nameof(identityValidator));
        _logger = Guard.NotNull(logger);
    }

    public Task<Result<ProcessModuleQueryResult>>
        GetModulesAsync(
            MonitoringSessionIdentity identity,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return RunAsync(
            () => EnumerateModules(identity, cancellationToken),
            "Module enumeration was cancelled.",
            "Target modules could not be enumerated.",
            cancellationToken);
    }

    public Task<Result<ProcessThreadQueryResult>>
        GetThreadsAsync(
            MonitoringSessionIdentity identity,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return RunAsync(
            () => EnumerateThreads(identity, cancellationToken),
            "Thread enumeration was cancelled.",
            "Target threads could not be enumerated.",
            cancellationToken);
    }

    private Result<ProcessModuleQueryResult> EnumerateModules(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken)
    {
        var validation = _identityValidator.Validate(identity);

        if (validation.IsFailure)
        {
            return Result<ProcessModuleQueryResult>.Failure(
                validation.Error);
        }

        var modules = new List<ProcessModuleInfo>();
        var warnings = new List<Error>();

        try
        {
            using var enumerator = _source
                .EnumerateModules(identity.ProcessId)
                .GetEnumerator();

            while (MoveNext(
                enumerator,
                modules.Count,
                "module",
                warnings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                modules.Add(ReadModule(
                    enumerator.Current,
                    modules.Count));
            }
        }
        catch (Exception exception) when (
            IsExpectedProcessException(exception))
        {
            return CompleteOrFail(
                modules,
                warnings,
                CreateEnumerationError(
                    "Module enumeration failed.",
                    exception),
                result => new ProcessModuleQueryResult(
                    result,
                    warnings));
        }

        LogWarnings(warnings);
        return Result<ProcessModuleQueryResult>.Success(
            new ProcessModuleQueryResult(modules, warnings));
    }

    private Result<ProcessThreadQueryResult> EnumerateThreads(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken)
    {
        var validation = _identityValidator.Validate(identity);

        if (validation.IsFailure)
        {
            return Result<ProcessThreadQueryResult>.Failure(
                validation.Error);
        }

        var threads = new List<ProcessThreadInfo>();
        var warnings = new List<Error>();

        try
        {
            using var enumerator = _source
                .EnumerateThreads(identity.ProcessId)
                .GetEnumerator();

            while (MoveNext(
                enumerator,
                threads.Count,
                "thread",
                warnings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                threads.Add(ReadThread(
                    enumerator.Current,
                    threads.Count));
            }
        }
        catch (Exception exception) when (
            IsExpectedProcessException(exception))
        {
            return CompleteOrFail(
                threads,
                warnings,
                CreateEnumerationError(
                    "Thread enumeration failed.",
                    exception),
                result => new ProcessThreadQueryResult(
                    result,
                    warnings));
        }

        LogWarnings(warnings);
        return Result<ProcessThreadQueryResult>.Success(
            new ProcessThreadQueryResult(threads, warnings));
    }

    private ProcessModuleInfo ReadModule(
        IWindowsModuleAccessor module,
        int index)
    {
        var warnings = new List<Error>();
        var name = ReadReferenceField(
                module.GetName,
                $"Module {index:N0} name",
                warnings)
            ?? $"<module {index:N0}>";

        if (string.IsNullOrWhiteSpace(name))
        {
            warnings.Add(
                CreateFieldError(
                    $"Module {index:N0} name",
                    new InvalidDataException(
                        "Module name was empty.")));
            name = $"<module {index:N0}>";
        }

        var baseAddress = ReadNullableField(
            () => unchecked(
                (ulong)module.GetBaseAddress().ToInt64()),
            $"{name} base address",
            warnings);
        var size = ReadNullableField(
            () => checked((ulong)module.GetSize()),
            $"{name} size",
            warnings);
        var path = ReadReferenceField(
            module.GetPath,
            $"{name} path",
            warnings);
        var version = ReadReferenceField(
            module.GetVersion,
            $"{name} version",
            warnings);
        LogWarnings(warnings);
        return new ProcessModuleInfo(
            name,
            baseAddress,
            size,
            path,
            version,
            warnings);
    }

    private ProcessThreadInfo ReadThread(
        IWindowsThreadAccessor thread,
        int index)
    {
        var warnings = new List<Error>();
        var threadId = ReadNullableField(
            thread.GetThreadId,
            $"Thread {index:N0} ID",
            warnings) ?? 0;
        var state = ReadReferenceField(
            thread.GetState,
            $"Thread {threadId} state",
            warnings);
        var priority = ReadNullableField(
            thread.GetPriority,
            $"Thread {threadId} priority",
            warnings);
        var startTime = ReadNullableField(
            thread.GetStartTime,
            $"Thread {threadId} start time",
            warnings);
        var cpuTime = ReadNullableField(
            thread.GetCpuTime,
            $"Thread {threadId} CPU time",
            warnings);

        if (threadId < 0)
        {
            warnings.Add(
                CreateFieldError(
                    $"Thread {index:N0} ID",
                    new InvalidDataException(
                        "Thread ID was negative.")));
            threadId = 0;
        }

        if (priority is < 0)
        {
            warnings.Add(
                CreateFieldError(
                    $"Thread {threadId} priority",
                    new InvalidDataException(
                        "Thread priority was negative.")));
            priority = null;
        }

        if (cpuTime.HasValue &&
            cpuTime.Value < TimeSpan.Zero)
        {
            warnings.Add(
                CreateFieldError(
                    $"Thread {threadId} CPU time",
                    new InvalidDataException(
                        "Thread CPU time was negative.")));
            cpuTime = null;
        }

        LogWarnings(warnings);
        return new ProcessThreadInfo(
            threadId,
            state,
            priority,
            startTime,
            cpuTime,
            warnings);
    }

    private static T? ReadReferenceField<T>(
        Func<T?> read,
        string fieldName,
        ICollection<Error> warnings)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (
            IsExpectedProcessException(exception))
        {
            warnings.Add(
                CreateFieldError(fieldName, exception));
            return default;
        }
    }

    private static T? ReadNullableField<T>(
        Func<T> read,
        string fieldName,
        ICollection<Error> warnings)
        where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (
            IsExpectedProcessException(exception))
        {
            warnings.Add(
                CreateFieldError(fieldName, exception));
            return null;
        }
    }

    private static bool MoveNext<T>(
        IEnumerator<T> enumerator,
        int completedCount,
        string itemName,
        ICollection<Error> warnings)
    {
        try
        {
            return enumerator.MoveNext();
        }
        catch (Exception exception) when (
            IsExpectedProcessException(exception))
        {
            var error = CreateEnumerationError(
                $"{itemName} enumeration stopped after " +
                $"{completedCount:N0} item(s).",
                exception);

            if (completedCount == 0)
            {
                throw;
            }

            warnings.Add(error);
            return false;
        }
    }

    private void LogWarnings(IEnumerable<Error> warnings)
    {
        foreach (var warning in warnings)
        {
            _ = _logger.Log(
                AppLogLevel.Warning,
                warning.ToDisplayMessage(),
                warning.Exception);
        }
    }

    private Result<TResult> CompleteOrFail<TItem, TResult>(
        IReadOnlyList<TItem> items,
        ICollection<Error> warnings,
        Error error,
        Func<IReadOnlyList<TItem>, TResult> create)
    {
        if (items.Count == 0)
        {
            return Result<TResult>.Failure(error);
        }

        warnings.Add(error);
        LogWarnings([error]);
        return Result<TResult>.Success(create(items));
    }

    private static Error CreateFieldError(
        string fieldName,
        Exception exception)
    {
        return new Error(
            MapErrorCode(exception),
            $"{fieldName} could not be read.",
            exception);
    }

    private static Error CreateEnumerationError(
        string message,
        Exception exception)
    {
        return new Error(
            MapErrorCode(exception),
            message,
            exception);
    }

    private static ErrorCode MapErrorCode(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => ErrorCode.AccessDenied,
            Win32Exception win32
                when win32.NativeErrorCode ==
                     NativeMemoryConstants.ErrorAccessDenied =>
                ErrorCode.AccessDenied,
            ArgumentException or
            InvalidOperationException => ErrorCode.NotFound,
            _ => ErrorCode.NativeApi,
        };
    }

    private static bool IsExpectedProcessException(
        Exception exception)
    {
        return exception is Win32Exception or
            UnauthorizedAccessException or
            InvalidOperationException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException or
            PlatformNotSupportedException or
            OverflowException;
    }

    private static async Task<Result<T>> RunAsync<T>(
        Func<Result<T>> operation,
        string cancellationMessage,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(operation, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<T>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    cancellationMessage,
                    exception));
        }
        catch (Exception exception) when (
            IsExpectedProcessException(exception))
        {
            return Result<T>.Failure(
                new Error(
                    MapErrorCode(exception),
                    failureMessage,
                    exception));
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Windows.Monitoring;

internal sealed class WindowsMonitoringTargetConnection
    : IMonitoringTargetConnection
{
    private Process? _process;

    public WindowsMonitoringTargetConnection(
        Process process,
        MonitoringSessionIdentity identity)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        Identity = identity ??
            throw new ArgumentNullException(nameof(identity));
    }

    public MonitoringSessionIdentity Identity { get; }

    public Task<Result<bool>> IsAliveAsync(
        CancellationToken cancellationToken = default)
    {
        var process = Volatile.Read(ref _process);

        if (process is null)
        {
            return Task.FromResult(
                Result<bool>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "The target process connection has been disposed.")));
        }

        return Task.Run(
            () => CheckLiveness(process, cancellationToken),
            cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _process, null)?.Dispose();
        return ValueTask.CompletedTask;
    }

    private Result<bool> CheckLiveness(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();

            if (process.HasExited)
            {
                return Result<bool>.Success(false);
            }

            var currentStartTime = new DateTimeOffset(process.StartTime);
            var identityMatches =
                currentStartTime.ToUniversalTime() ==
                Identity.ProcessStartTime.ToUniversalTime() &&
                string.Equals(
                    process.ProcessName,
                    Identity.ProcessName,
                    StringComparison.OrdinalIgnoreCase);

            return identityMatches
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "The target process identity is no longer valid."));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<bool>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "The target liveness check was cancelled.",
                    exception));
        }
        catch (InvalidOperationException)
        {
            return Result<bool>.Success(false);
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == 5)
        {
            return Result<bool>.Failure(
                new Error(
                    ErrorCode.AccessDenied,
                    "Access to the target process was denied.",
                    exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result<bool>.Failure(
                new Error(
                    ErrorCode.AccessDenied,
                    "Access to the target process was denied.",
                    exception));
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            NotSupportedException or
            PlatformNotSupportedException)
        {
            return Result<bool>.Failure(
                new Error(
                    ErrorCode.NativeApi,
                    "The target process could not be checked.",
                    exception));
        }
    }
}

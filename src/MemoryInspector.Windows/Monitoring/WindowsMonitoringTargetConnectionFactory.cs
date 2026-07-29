using System.ComponentModel;
using System.Diagnostics;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Windows.Processes;

namespace MemoryInspector.Windows.Monitoring;

public sealed class WindowsMonitoringTargetConnectionFactory
    : IMonitoringTargetConnectionFactory
{
    public Task<Result<IMonitoringTargetConnection>> ConnectAsync(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return Task.Run(
            () => Connect(identity, cancellationToken),
            cancellationToken);
    }

    private static Result<IMonitoringTargetConnection> Connect(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken)
    {
        Process? process = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process = Process.GetProcessById(identity.ProcessId);

            if (process.HasExited)
            {
                return Failure(
                    process,
                    ErrorCode.NotFound,
                    "The target process has already exited.");
            }

            var actualStartTime = new DateTimeOffset(process.StartTime);
            var actualName = process.ProcessName;
            var actualArchitecture =
                ProcessArchitectureDetector.Detect(identity.ProcessId);

            if (actualStartTime.ToUniversalTime() !=
                    identity.ProcessStartTime.ToUniversalTime() ||
                !string.Equals(
                    actualName,
                    identity.ProcessName,
                    StringComparison.OrdinalIgnoreCase) ||
                actualArchitecture != identity.Architecture)
            {
                return Failure(
                    process,
                    ErrorCode.InvalidState,
                    "The target process identity changed before monitoring began.");
            }

            IMonitoringTargetConnection connection =
                new WindowsMonitoringTargetConnection(process, identity);
            process = null;

            return Result<IMonitoringTargetConnection>.Success(connection);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                process,
                ErrorCode.Cancelled,
                "Connecting to the target process was cancelled.",
                exception);
        }
        catch (ArgumentException exception)
        {
            return Failure(
                process,
                ErrorCode.NotFound,
                "The target process no longer exists.",
                exception);
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == 5)
        {
            return Failure(
                process,
                ErrorCode.AccessDenied,
                "Access to the target process was denied.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(
                process,
                ErrorCode.AccessDenied,
                "Access to the target process was denied.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(
                process,
                ErrorCode.NotFound,
                "The target process exited while the connection was created.",
                exception);
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            NotSupportedException or
            PlatformNotSupportedException)
        {
            return Failure(
                process,
                ErrorCode.NativeApi,
                "The target process could not be opened.",
                exception);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static Result<IMonitoringTargetConnection> Failure(
        Process? process,
        ErrorCode code,
        string message,
        Exception? exception = null)
    {
        process?.Dispose();
        return Result<IMonitoringTargetConnection>.Failure(
            new Error(code, message, exception));
    }
}

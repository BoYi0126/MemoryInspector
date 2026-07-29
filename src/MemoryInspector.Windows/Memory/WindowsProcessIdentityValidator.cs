using System.ComponentModel;
using System.Diagnostics;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Windows.Processes;

namespace MemoryInspector.Windows.Memory;

internal sealed class WindowsProcessIdentityValidator
    : IProcessIdentityValidator
{
    public Result Validate(MonitoringSessionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);

            if (process.HasExited)
            {
                return Exited();
            }

            var startTime = new DateTimeOffset(process.StartTime);
            var architecture =
                ProcessArchitectureDetector.Detect(identity.ProcessId);

            if (startTime.ToUniversalTime() !=
                    identity.ProcessStartTime.ToUniversalTime() ||
                !string.Equals(
                    process.ProcessName,
                    identity.ProcessName,
                    StringComparison.OrdinalIgnoreCase) ||
                architecture != identity.Architecture)
            {
                return Result.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "The monitoring target identity is no longer valid."));
            }

            return Result.Success();
        }
        catch (ArgumentException exception)
        {
            return Exited(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Exited(exception);
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode ==
                  NativeMemoryConstants.ErrorAccessDenied)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.AccessDenied,
                    "Access to the monitoring target was denied.",
                    exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.AccessDenied,
                    "Access to the monitoring target was denied.",
                    exception));
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            NotSupportedException or
            PlatformNotSupportedException)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.NativeApi,
                    "The monitoring target identity could not be validated.",
                    exception));
        }
    }

    private static Result Exited(Exception? exception = null)
    {
        return Result.Failure(
            new Error(
                ErrorCode.NotFound,
                "The monitoring target process has exited.",
                exception));
    }
}

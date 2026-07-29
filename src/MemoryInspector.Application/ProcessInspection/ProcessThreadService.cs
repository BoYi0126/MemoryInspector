using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.ProcessInspection;

public sealed class ProcessThreadService(
    IMonitoringSessionService monitoringSessionService,
    IProcessThreadProvider provider) : IProcessThreadService
{
    private readonly IMonitoringSessionService _sessionService =
        Guard.NotNull(monitoringSessionService);
    private readonly IProcessThreadProvider _provider =
        Guard.NotNull(provider);

    public async Task<Result<ProcessThreadQueryResult>>
        GetThreadsAsync(
            CancellationToken cancellationToken = default)
    {
        var session = _sessionService.CurrentSession;

        if (session?.State != MonitoringSessionState.Connected)
        {
            return InvalidState();
        }

        var result = await _provider.GetThreadsAsync(
                session.Identity,
                cancellationToken)
            .ConfigureAwait(false);
        return IsSameSession(session)
            ? result
            : InvalidState();
    }

    private bool IsSameSession(MonitoringSession expected)
    {
        var current = _sessionService.CurrentSession;
        return current?.SessionId == expected.SessionId &&
               current.State == MonitoringSessionState.Connected &&
               current.Identity == expected.Identity;
    }

    private static Result<ProcessThreadQueryResult> InvalidState()
    {
        return Result<ProcessThreadQueryResult>.Failure(
            new Error(
                ErrorCode.InvalidState,
                "A stable connected monitoring session is required " +
                "to enumerate threads."));
    }
}

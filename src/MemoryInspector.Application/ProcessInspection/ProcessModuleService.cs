using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.ProcessInspection;

public sealed class ProcessModuleService(
    IMonitoringSessionService monitoringSessionService,
    IProcessModuleProvider provider) : IProcessModuleService
{
    private readonly IMonitoringSessionService _sessionService =
        Guard.NotNull(monitoringSessionService);
    private readonly IProcessModuleProvider _provider =
        Guard.NotNull(provider);

    public async Task<Result<ProcessModuleQueryResult>>
        GetModulesAsync(
            CancellationToken cancellationToken = default)
    {
        var session = _sessionService.CurrentSession;

        if (session?.State != MonitoringSessionState.Connected)
        {
            return InvalidState();
        }

        var result = await _provider.GetModulesAsync(
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

    private static Result<ProcessModuleQueryResult> InvalidState()
    {
        return Result<ProcessModuleQueryResult>.Failure(
            new Error(
                ErrorCode.InvalidState,
                "A stable connected monitoring session is required " +
                "to enumerate modules."));
    }
}

using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.Memory;

public sealed class MemoryRegionService(
    IMonitoringSessionService monitoringSessionService,
    IMemoryRegionProvider provider) : IMemoryRegionService
{
    private readonly IMonitoringSessionService _monitoringSessionService =
        Guard.NotNull(monitoringSessionService);
    private readonly IMemoryRegionProvider _provider = Guard.NotNull(provider);

    public async Task<Result<MemoryRegionQueryResult>> GetRegionsAsync(
        CancellationToken cancellationToken = default)
    {
        var session = _monitoringSessionService.CurrentSession;

        if (session?.State != MonitoringSessionState.Connected)
        {
            return Result<MemoryRegionQueryResult>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "A connected monitoring session is required."));
        }

        var result = await _provider.GetRegionsAsync(
            session.Identity,
            cancellationToken);
        var currentSession = _monitoringSessionService.CurrentSession;

        if (currentSession?.SessionId != session.SessionId ||
            currentSession.State != MonitoringSessionState.Connected ||
            currentSession.Identity != session.Identity)
        {
            return Result<MemoryRegionQueryResult>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "The monitoring session changed during the memory region query."));
        }

        return result;
    }
}

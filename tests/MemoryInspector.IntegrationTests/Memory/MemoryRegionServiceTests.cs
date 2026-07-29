using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.IntegrationTests.Memory;

[TestClass]
public sealed class MemoryRegionServiceTests
{
    private static readonly MonitoringSessionIdentity Identity = new(
        42,
        new DateTimeOffset(2026, 7, 29, 8, 30, 0, TimeSpan.Zero),
        ProcessArchitecture.X64,
        "Target");

    [TestMethod]
    public async Task ConnectedSessionIsPassedToProvider()
    {
        var sessionService = new StubSessionService
        {
            CurrentSession = CreateSession(
                MonitoringSessionState.Connected),
        };
        MonitoringSessionIdentity? receivedIdentity = null;
        var provider = new DelegateMemoryRegionProvider(
            identity =>
            {
                receivedIdentity = identity;
                return Result<MemoryRegionQueryResult>.Success(
                    new MemoryRegionQueryResult([CreateRegion()]));
            });
        var service = new MemoryRegionService(sessionService, provider);

        var result = await service.GetRegionsAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Identity, receivedIdentity);
        Assert.AreEqual(1, result.Value.Regions.Count);
    }

    [TestMethod]
    public async Task DisconnectedSessionIsRejectedBeforeProviderCall()
    {
        var sessionService = new StubSessionService
        {
            CurrentSession = CreateSession(
                MonitoringSessionState.Disconnected),
        };
        var provider = new DelegateMemoryRegionProvider(
            _ => throw new AssertFailedException(
                "Provider must not be called."));
        var service = new MemoryRegionService(sessionService, provider);

        var result = await service.GetRegionsAsync();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.AreEqual(0, provider.CallCount);
    }

    [TestMethod]
    public async Task SessionChangeDuringQueryInvalidatesTheResult()
    {
        var original = CreateSession(MonitoringSessionState.Connected);
        var sessionService = new StubSessionService
        {
            CurrentSession = original,
        };
        var provider = new DelegateMemoryRegionProvider(
            _ =>
            {
                sessionService.CurrentSession = original with
                {
                    SessionId = Guid.NewGuid(),
                };
                return Result<MemoryRegionQueryResult>.Success(
                    new MemoryRegionQueryResult([CreateRegion()]));
            });
        var service = new MemoryRegionService(sessionService, provider);

        var result = await service.GetRegionsAsync();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
    }

    private static MonitoringSession CreateSession(
        MonitoringSessionState state)
    {
        return new MonitoringSession
        {
            SessionId = Guid.NewGuid(),
            Identity = Identity,
            State = state,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static MemoryRegion CreateRegion()
    {
        return new MemoryRegion(
            0x1_000,
            0x1_000,
            0x1_000,
            MemoryRegionState.Committed,
            MemoryRegionType.Private,
            MemoryProtection.ReadWrite);
    }

    private sealed class DelegateMemoryRegionProvider(
        Func<
            MonitoringSessionIdentity,
            Result<MemoryRegionQueryResult>> getRegions)
        : IMemoryRegionProvider
    {
        public int CallCount { get; private set; }

        public Task<Result<MemoryRegionQueryResult>> GetRegionsAsync(
            MonitoringSessionIdentity identity,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(getRegions(identity));
        }
    }

    private sealed class StubSessionService : IMonitoringSessionService
    {
        public MonitoringSession? CurrentSession { get; set; }

        public event EventHandler<MonitoringSessionChangedEventArgs>?
            SessionChanged
        {
            add { }
            remove { }
        }

        public Task<Result<MonitoringSession>> StartAsync(
            MonitoringSessionIdentity identity,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<MonitoringSession>> CheckLivenessAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> StopAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

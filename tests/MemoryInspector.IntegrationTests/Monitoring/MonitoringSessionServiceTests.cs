using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.IntegrationTests.ProcessExplorer;

namespace MemoryInspector.IntegrationTests.Monitoring;

[TestClass]
public sealed class MonitoringSessionServiceTests
{
    private static readonly MonitoringSessionIdentity Identity = new(
        42,
        new DateTimeOffset(2026, 7, 29, 8, 30, 0, TimeSpan.Zero),
        ProcessArchitecture.X64,
        "Target");

    [TestMethod]
    public async Task StartCreatesConnectedSessionWithCompleteIdentity()
    {
        var connection = new FakeConnection(Identity);
        await using var service = CreateService(
            new FakeConnectionFactory(
                Result<IMonitoringTargetConnection>.Success(connection)));

        var result = await service.StartAsync(Identity);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(MonitoringSessionState.Connected, result.Value.State);
        Assert.AreEqual(Identity, result.Value.Identity);
        Assert.IsNotNull(result.Value.ConnectedAt);
        Assert.IsTrue(result.Value.IsActive);
    }

    [TestMethod]
    public async Task StartRejectsASecondDifferentActiveSession()
    {
        var connection = new FakeConnection(Identity);
        var factory = new FakeConnectionFactory(
            Result<IMonitoringTargetConnection>.Success(connection));
        await using var service = CreateService(factory);
        await service.StartAsync(Identity);
        var other = new MonitoringSessionIdentity(
            43,
            Identity.ProcessStartTime,
            Identity.Architecture,
            Identity.ProcessName);

        var result = await service.StartAsync(other);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.AreEqual(1, factory.CallCount);
        Assert.IsFalse(connection.IsDisposed);
        Assert.AreEqual(Identity, service.CurrentSession!.Identity);
    }

    [TestMethod]
    public async Task StartingTheSameTargetIsIdempotent()
    {
        var factory = new FakeConnectionFactory(
            Result<IMonitoringTargetConnection>.Success(
                new FakeConnection(Identity)));
        await using var service = CreateService(factory);
        var first = await service.StartAsync(Identity);

        var second = await service.StartAsync(Identity);

        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(first.Value.SessionId, second.Value.SessionId);
        Assert.AreEqual(1, factory.CallCount);
    }

    [TestMethod]
    public async Task StopDisconnectsAndDisposesTargetResource()
    {
        var connection = new FakeConnection(Identity);
        await using var service = CreateService(
            new FakeConnectionFactory(
                Result<IMonitoringTargetConnection>.Success(connection)));
        await service.StartAsync(Identity);

        var result = await service.StopAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(connection.IsDisposed);
        Assert.AreEqual(
            MonitoringSessionState.Disconnected,
            service.CurrentSession!.State);
        Assert.IsFalse(service.CurrentSession.IsActive);
    }

    [TestMethod]
    public async Task LivenessFailureInvalidatesExitedTargetAndDisposesResource()
    {
        var connection = new FakeConnection(
            Identity,
            Result<bool>.Success(false));
        await using var service = CreateService(
            new FakeConnectionFactory(
                Result<IMonitoringTargetConnection>.Success(connection)));
        await service.StartAsync(Identity);

        var result = await service.CheckLivenessAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            MonitoringSessionState.TargetExited,
            result.Value.State);
        Assert.IsTrue(connection.IsDisposed);
        Assert.IsFalse(result.Value.IsActive);
    }

    [TestMethod]
    public async Task AccessDeniedIsRetainedAsATerminalSessionState()
    {
        var error = new Error(
            ErrorCode.AccessDenied,
            "Access to the target process was denied.");
        await using var service = CreateService(
            new FakeConnectionFactory(
                Result<IMonitoringTargetConnection>.Failure(error)));

        var result = await service.StartAsync(Identity);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(
            MonitoringSessionState.AccessDenied,
            service.CurrentSession!.State);
        Assert.IsNotNull(service.CurrentSession.EndedAt);
    }

    [TestMethod]
    public async Task MismatchedConnectionIdentityIsInvalidatedAndDisposed()
    {
        var mismatchedIdentity = new MonitoringSessionIdentity(
            Identity.ProcessId,
            Identity.ProcessStartTime.AddSeconds(1),
            Identity.Architecture,
            Identity.ProcessName);
        var connection = new FakeConnection(mismatchedIdentity);
        await using var service = CreateService(
            new FakeConnectionFactory(
                Result<IMonitoringTargetConnection>.Success(connection)));

        var result = await service.StartAsync(Identity);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(
            MonitoringSessionState.Invalidated,
            service.CurrentSession!.State);
        Assert.IsTrue(connection.IsDisposed);
    }

    [TestMethod]
    public async Task BackgroundMonitorAutomaticallyDetectsTargetExit()
    {
        var exited = new TaskCompletionSource<MonitoringSession>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new FakeConnection(
            Identity,
            Result<bool>.Success(false));
        await using var service = CreateService(
            new FakeConnectionFactory(
                Result<IMonitoringTargetConnection>.Success(connection)),
            TimeSpan.FromMilliseconds(10));
        service.SessionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Session.State ==
                MonitoringSessionState.TargetExited)
            {
                exited.TrySetResult(eventArgs.Session);
            }
        };

        await service.StartAsync(Identity);
        var session = await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(MonitoringSessionState.TargetExited, session.State);
        Assert.IsTrue(connection.IsDisposed);
    }

    private static MonitoringSessionService CreateService(
        IMonitoringTargetConnectionFactory factory,
        TimeSpan? interval = null)
    {
        return new MonitoringSessionService(
            factory,
            new TestLogger(),
            TimeProvider.System,
            interval ?? TimeSpan.FromHours(1));
    }

    private sealed class FakeConnectionFactory(
        Result<IMonitoringTargetConnection> result)
        : IMonitoringTargetConnectionFactory
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<Result<IMonitoringTargetConnection>> ConnectAsync(
            MonitoringSessionIdentity identity,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeConnection(
        MonitoringSessionIdentity identity,
        Result<bool>? livenessResult = null)
        : IMonitoringTargetConnection
    {
        public MonitoringSessionIdentity Identity { get; } = identity;

        public bool IsDisposed { get; private set; }

        public Task<Result<bool>> IsAliveAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                livenessResult ?? Result<bool>.Success(true));
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

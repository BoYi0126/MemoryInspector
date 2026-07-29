using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.Core.Tests.Monitoring;

[TestClass]
public sealed class MonitoringSessionIdentityTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 7, 29, 8, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void IdentityContainsEveryRequiredProcessField()
    {
        var identity = new MonitoringSessionIdentity(
            42,
            StartTime,
            ProcessArchitecture.X64,
            "Target");

        Assert.AreEqual(42, identity.ProcessId);
        Assert.AreEqual(StartTime, identity.ProcessStartTime);
        Assert.AreEqual(ProcessArchitecture.X64, identity.Architecture);
        Assert.AreEqual("Target", identity.ProcessName);
    }

    [TestMethod]
    public void IdentityRejectsUnknownArchitecture()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new MonitoringSessionIdentity(
                42,
                StartTime,
                ProcessArchitecture.Unknown,
                "Target"));
    }

    [TestMethod]
    public void RecordEqualityUsesTheCompleteIdentity()
    {
        var identity = new MonitoringSessionIdentity(
            42,
            StartTime,
            ProcessArchitecture.X64,
            "Target");

        Assert.AreNotEqual(
            identity,
            new MonitoringSessionIdentity(
                42,
                StartTime.AddSeconds(1),
                ProcessArchitecture.X64,
                "Target"));
        Assert.AreNotEqual(
            identity,
            new MonitoringSessionIdentity(
                42,
                StartTime,
                ProcessArchitecture.X86,
                "Target"));
        Assert.AreNotEqual(
            identity,
            new MonitoringSessionIdentity(
                42,
                StartTime,
                ProcessArchitecture.X64,
                "Other"));
    }

    [TestMethod]
    public void SessionIsActiveOnlyWhileConnectingOrConnected()
    {
        var identity = new MonitoringSessionIdentity(
            42,
            StartTime,
            ProcessArchitecture.X64,
            "Target");
        var session = new MonitoringSession
        {
            SessionId = Guid.NewGuid(),
            Identity = identity,
            State = MonitoringSessionState.Connecting,
            CreatedAt = StartTime,
        };

        Assert.IsTrue(session.IsActive);
        Assert.IsTrue((session with
        {
            State = MonitoringSessionState.Connected,
        }).IsActive);
        Assert.IsFalse((session with
        {
            State = MonitoringSessionState.TargetExited,
        }).IsActive);
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Windows.Monitoring;

namespace MemoryInspector.Windows.Tests.Monitoring;

[TestClass]
public sealed class WindowsMonitoringTargetConnectionFactoryTests
{
    [TestMethod]
    public async Task ConnectsToCurrentProcessAndReportsItAlive()
    {
        using var process = Process.GetCurrentProcess();
        var identity = CreateIdentity(process);
        var factory = new WindowsMonitoringTargetConnectionFactory();

        var result = await factory.ConnectAsync(identity);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(identity, result.Value.Identity);
        var liveness = await result.Value.IsAliveAsync();
        Assert.IsTrue(liveness.IsSuccess);
        Assert.IsTrue(liveness.Value);
        await result.Value.DisposeAsync();
    }

    [TestMethod]
    public async Task RejectsAStaleIdentityForAReusedPid()
    {
        using var process = Process.GetCurrentProcess();
        var actual = CreateIdentity(process);
        var stale = new MonitoringSessionIdentity(
            actual.ProcessId,
            actual.ProcessStartTime.AddSeconds(-1),
            actual.Architecture,
            actual.ProcessName);
        var factory = new WindowsMonitoringTargetConnectionFactory();

        var result = await factory.ConnectAsync(stale);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(
            MemoryInspector.Common.ErrorCode.InvalidState,
            result.Error.Code);
    }

    private static MonitoringSessionIdentity CreateIdentity(Process process)
    {
        return new MonitoringSessionIdentity(
            process.Id,
            new DateTimeOffset(process.StartTime),
            RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => ProcessArchitecture.X86,
                Architecture.X64 => ProcessArchitecture.X64,
                Architecture.Arm => ProcessArchitecture.Arm32,
                Architecture.Arm64 => ProcessArchitecture.Arm64,
                _ => throw new PlatformNotSupportedException(),
            },
            process.ProcessName);
    }
}

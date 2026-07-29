using System.ComponentModel;
using System.Diagnostics;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Processes;
using MemoryInspector.Common;
using MemoryInspector.Core.Processes;
using MemoryInspector.Windows.Processes;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.Processes;

[TestClass]
public sealed class SystemProcessServiceTests
{
    [TestMethod]
    public async Task EmptySourceReturnsAnEmptySuccessfulList()
    {
        var source = new FakeProcessSource(() => Array.Empty<IProcessAccessor>());
        using var service = CreateService(source);

        var result = await service.GetProcessesAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Value.Count);
    }

    [TestMethod]
    public async Task EnumerationReportsKnownTotalAndCompletedCount()
    {
        var source = new FakeProcessSource(
            () =>
            [
                new FakeProcessAccessor { ProcessId = 1 },
                new FakeProcessAccessor { ProcessId = 2 },
            ]);
        var reports = new List<ProcessScanProgress>();
        var progress = new SynchronousProgress<ProcessScanProgress>(
            reports.Add);
        using var service = CreateService(source);

        var result = await service.GetProcessesAsync(progress: progress);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[]
            {
                new ProcessScanProgress(0, 2),
                new ProcessScanProgress(1, 2),
                new ProcessScanProgress(2, 2),
            },
            reports);
    }

    [TestMethod]
    public async Task ExitedProcessDoesNotAbortEnumeration()
    {
        var exited = new FakeProcessAccessor
        {
            ProcessId = 321,
            HasExitedFactory = () => true,
            ProcessNameFactory = () => throw new InvalidOperationException(),
        };
        var available = new FakeProcessAccessor
        {
            ProcessId = 654,
            ProcessNameFactory = () => "Available",
        };
        var source = new FakeProcessSource(() => [exited, available]);
        using var service = CreateService(source);

        var result = await service.GetProcessesAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, result.Value.Count);
        var exitedSummary = result.Value.Single(item => item.ProcessId == 321);
        Assert.AreEqual(ProcessAccessStatus.Exited, exitedSummary.AccessStatus);
        Assert.IsTrue(exited.IsDisposed);
        Assert.IsTrue(available.IsDisposed);
    }

    [TestMethod]
    public async Task AccessDeniedFieldProducesAPartialSummary()
    {
        var inaccessible = new FakeProcessAccessor
        {
            ProcessId = 111,
            ExecutablePathFactory =
                () => throw new Win32Exception(5, "Access denied."),
        };
        var source = new FakeProcessSource(() => [inaccessible]);
        using var service = CreateService(source);

        var result = await service.GetProcessesAsync();

        Assert.IsTrue(result.IsSuccess);
        var summary = result.Value.Single();
        Assert.AreEqual(ProcessAccessStatus.AccessDenied, summary.AccessStatus);
        Assert.IsNull(summary.ExecutablePath);
        Assert.AreEqual(1_024L, summary.WorkingSetBytes);
    }

    [TestMethod]
    public async Task UnexpectedFieldFailureDoesNotHideOtherProcesses()
    {
        var partial = new FakeProcessAccessor
        {
            ProcessId = 1,
            ProcessNameFactory = () => "Partial",
            WorkingSetBytesFactory =
                () => throw new NotSupportedException("Unavailable."),
        };
        var available = new FakeProcessAccessor
        {
            ProcessId = 2,
            ProcessNameFactory = () => "Available",
        };
        var logger = new RecordingLogger();
        var source = new FakeProcessSource(() => [partial, available]);
        using var service = CreateService(source, logger);

        var result = await service.GetProcessesAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, result.Value.Count);
        Assert.AreEqual(
            ProcessAccessStatus.Partial,
            result.Value.Single(item => item.ProcessId == 1).AccessStatus);
        Assert.AreEqual(
            ProcessAccessStatus.Available,
            result.Value.Single(item => item.ProcessId == 2).AccessStatus);
        Assert.IsTrue(
            logger.Entries.Any(entry =>
                entry.Level == AppLogLevel.Warning));
    }

    [TestMethod]
    public async Task CancellationIsReturnedAsAResult()
    {
        var source = new FakeProcessSource(() => Array.Empty<IProcessAccessor>());
        using var service = CreateService(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.GetProcessesAsync(cancellation.Token);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Cancelled, result.Error.Code);
        Assert.AreEqual(0, source.CallCount);
    }

    [TestMethod]
    public async Task CpuUsageUsesSamplesFromConsecutiveRefreshes()
    {
        var totalProcessorTime = TimeSpan.Zero;
        var startTime = new DateTime(
            2026,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        var source = new FakeProcessSource(
            () =>
            [
                new FakeProcessAccessor
                {
                    ProcessId = 777,
                    StartTimeFactory = () => startTime,
                    TotalProcessorTimeFactory = () => totalProcessorTime,
                },
            ]);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero));
        using var service = new SystemProcessService(
            source,
            new RecordingLogger(),
            timeProvider,
            processorCount: 4);

        var first = await service.GetProcessesAsync();
        totalProcessorTime = TimeSpan.FromSeconds(1);
        timeProvider.SetUtcNow(
            new DateTimeOffset(2026, 1, 1, 1, 0, 1, TimeSpan.Zero));
        var second = await service.GetProcessesAsync();

        Assert.IsNull(first.Value.Single().CpuUsagePercentage);
        Assert.AreEqual(25d, second.Value.Single().CpuUsagePercentage);
    }

    [TestMethod]
    public async Task MemoryValuesRemainRawBytesForCommonFormatting()
    {
        var source = new FakeProcessSource(
            () =>
            [
                new FakeProcessAccessor
                {
                    WorkingSetBytesFactory = () => 1_536,
                },
            ]);
        using var service = CreateService(source);

        var result = await service.GetProcessesAsync();
        var workingSet = result.Value.Single().WorkingSetBytes;

        Assert.IsNotNull(workingSet);
        Assert.AreEqual("1.5 KB", ByteSizeFormatter.Format(workingSet.Value));
    }

    [TestMethod]
    public async Task LiveServiceIncludesTheCurrentProcess()
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var service = new SystemProcessService(new RecordingLogger());

        var result = await service.GetProcessesAsync();

        Assert.IsTrue(
            result.IsSuccess,
            result.IsFailure ? result.Error.ToDisplayMessage() : null);
        var current = result.Value.Single(item =>
            item.ProcessId == currentProcess.Id);
        Assert.IsFalse(string.IsNullOrWhiteSpace(current.ProcessName));
        Assert.AreEqual(ProcessAccessStatus.Available, current.AccessStatus);
        Assert.IsNotNull(current.StartTime);
        Assert.IsNotNull(current.WorkingSetBytes);
    }

    private static SystemProcessService CreateService(
        IProcessSource source,
        RecordingLogger? logger = null)
    {
        return new SystemProcessService(
            source,
            logger ?? new RecordingLogger(),
            new ManualTimeProvider(
                new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero)),
            processorCount: 4);
    }

    private sealed class SynchronousProgress<T>(Action<T> report)
        : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }
}

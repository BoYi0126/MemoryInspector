using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Processes;
using MemoryInspector.Common;
using MemoryInspector.Core.Processes;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.IntegrationTests.ProcessExplorer;

[TestClass]
public sealed class ProcessExplorerViewModelTests
{
    [TestMethod]
    public async Task InitializeDoesNotScanProcesses()
    {
        var service = new QueueProcessService(
            [ProcessSummaryFactory.Create(30, "Zulu")]);
        using var viewModel = new ProcessExplorerViewModel(
            service,
            new TestLogger());

        await viewModel.InitializeAsync(AppSettings.CreateDefault());

        Assert.AreEqual(0, service.CallCount);
        Assert.AreEqual(0, viewModel.Processes.Count);
        Assert.AreEqual("Not scanned yet", viewModel.ProcessCountDisplay);
        Assert.IsNull(viewModel.LastRefreshedAt);
        StringAssert.Contains(viewModel.StatusMessage, "Scan Processes");
    }

    [TestMethod]
    public async Task ManualScanLoadsAndFormatsProcessesWithoutBlockingFilters()
    {
        var service = new QueueProcessService(
            [
                ProcessSummaryFactory.Create(30, "Zulu", workingSet: 1_536),
                ProcessSummaryFactory.Create(10, "Alpha"),
                ProcessSummaryFactory.Create(20, "Beta"),
            ]);
        using var viewModel = new ProcessExplorerViewModel(
            service,
            new TestLogger());

        await viewModel.InitializeAsync(AppSettings.CreateDefault());
        await viewModel.RefreshAsync();

        CollectionAssert.AreEqual(
            new[] { "Alpha", "Beta", "Zulu" },
            viewModel.Processes.Select(row => row.ProcessName).ToArray());
        Assert.AreEqual("1.5 KB", viewModel.Processes[2].WorkingSetDisplay);
        Assert.AreEqual("3 of 3 processes", viewModel.ProcessCountDisplay);
        Assert.IsNotNull(viewModel.LastRefreshedAt);
    }

    [TestMethod]
    public async Task SearchAndPidFilterAreAppliedImmediately()
    {
        var service = new QueueProcessService(
            [
                ProcessSummaryFactory.Create(100, "Alpha"),
                ProcessSummaryFactory.Create(200, "Beta"),
                ProcessSummaryFactory.Create(300, "Gamma"),
            ]);
        using var viewModel = new ProcessExplorerViewModel(
            service,
            new TestLogger());
        await viewModel.InitializeAsync(AppSettings.CreateDefault());
        await viewModel.RefreshAsync();

        viewModel.SearchText = "amm";
        Assert.AreEqual("Gamma", viewModel.Processes.Single().ProcessName);

        viewModel.SearchText = string.Empty;
        viewModel.PidFilterText = "200";
        Assert.AreEqual(200, viewModel.Processes.Single().ProcessId);

        viewModel.PidFilterText = "not-a-pid";
        Assert.AreEqual(0, viewModel.Processes.Count);
        Assert.IsNotNull(viewModel.FilterMessage);
    }

    [TestMethod]
    public async Task SortKeepsUnknownNumericValuesLast()
    {
        var service = new QueueProcessService(
            [
                ProcessSummaryFactory.Create(1, "Small", workingSet: 100),
                ProcessSummaryFactory.Create(2, "Unknown", workingSet: null),
                ProcessSummaryFactory.Create(3, "Large", workingSet: 1_000),
            ]);
        using var viewModel = new ProcessExplorerViewModel(
            service,
            new TestLogger());
        await viewModel.InitializeAsync(AppSettings.CreateDefault());
        await viewModel.RefreshAsync();

        viewModel.SelectedSortOption = ProcessSortOption.WorkingSet;
        viewModel.SortDescending = true;

        CollectionAssert.AreEqual(
            new[] { "Large", "Small", "Unknown" },
            viewModel.Processes.Select(row => row.ProcessName).ToArray());
    }

    [TestMethod]
    public async Task RefreshPreservesIdentityAndMarksDisappearedSelection()
    {
        var identityTime = new DateTimeOffset(
            2026,
            1,
            1,
            1,
            0,
            0,
            TimeSpan.Zero);
        var service = new QueueProcessService(
            [
                ProcessSummaryFactory.Create(
                    123,
                    "Target",
                    identityTime,
                    cpu: 1),
            ],
            [
                ProcessSummaryFactory.Create(
                    123,
                    "Target",
                    identityTime,
                    cpu: 25),
            ],
            Array.Empty<ProcessSummary>());
        using var viewModel = new ProcessExplorerViewModel(
            service,
            new TestLogger());
        await viewModel.InitializeAsync(AppSettings.CreateDefault());
        await viewModel.RefreshAsync();
        viewModel.SelectedProcess = viewModel.Processes.Single();

        await viewModel.RefreshAsync();

        Assert.IsNotNull(viewModel.SelectedProcess);
        Assert.AreEqual(25d, viewModel.SelectedProcess.CpuUsagePercentage);
        Assert.IsFalse(viewModel.SelectedProcess.IsStale);

        await viewModel.RefreshAsync();

        Assert.IsNotNull(viewModel.SelectedProcess);
        Assert.IsTrue(viewModel.SelectedProcess.IsStale);
        Assert.AreEqual(
            ProcessAccessStatus.Exited,
            viewModel.SelectedProcess.AccessStatus);
        Assert.IsTrue(viewModel.Processes.Single().IsStale);
        Assert.IsFalse(viewModel.StartMonitoringCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task StartMonitoringRaisesThePhaseSixBoundaryEvent()
    {
        var service = new QueueProcessService(
            [ProcessSummaryFactory.Create(42, "Target")]);
        using var viewModel = new ProcessExplorerViewModel(
            service,
            new TestLogger());
        await viewModel.InitializeAsync(AppSettings.CreateDefault());
        await viewModel.RefreshAsync();
        viewModel.SelectedProcess = viewModel.Processes.Single();
        ProcessMonitoringRequestedEventArgs? requested = null;
        viewModel.StartMonitoringRequested += (_, eventArgs) =>
            requested = eventArgs;

        viewModel.StartMonitoringCommand.Execute(null);

        Assert.IsNotNull(requested);
        Assert.AreEqual(42, requested.Process.ProcessId);
        Assert.IsTrue(
            viewModel.StatusMessage.Contains(
                "Monitoring requested",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MonitoringCommandsStartAndStopTheSession()
    {
        var processService = new QueueProcessService(
            [ProcessSummaryFactory.Create(42, "Target")]);
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = new ProcessExplorerViewModel(
            processService,
            new TestLogger(),
            monitoringService);
        await viewModel.InitializeAsync(AppSettings.CreateDefault());
        await viewModel.RefreshAsync();
        viewModel.SelectedProcess = viewModel.Processes.Single();

        await viewModel.StartMonitoringCommand.ExecuteAsync();

        Assert.IsNotNull(monitoringService.StartedIdentity);
        Assert.AreEqual(42, monitoringService.StartedIdentity.ProcessId);
        Assert.AreEqual("Target", monitoringService.StartedIdentity.ProcessName);
        Assert.AreEqual(
            ProcessArchitecture.X64,
            monitoringService.StartedIdentity.Architecture);
        Assert.AreEqual("Connected", viewModel.SessionStateDisplay);
        Assert.IsTrue(viewModel.IsSessionActive);

        await viewModel.StopMonitoringCommand.ExecuteAsync();

        Assert.AreEqual(1, monitoringService.StopCount);
        Assert.AreEqual("Disconnected", viewModel.SessionStateDisplay);
        Assert.IsFalse(viewModel.IsSessionActive);
    }

    [TestMethod]
    public async Task RefreshRemainsAsynchronousWhileTheServiceIsRunning()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<
            Result<IReadOnlyList<ProcessSummary>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new DelegateProcessService(
            cancellationToken =>
            {
                started.TrySetResult();
                cancellationToken.Register(() =>
                    completion.TrySetCanceled(cancellationToken));
                return completion.Task;
            });
        using var viewModel = new ProcessExplorerViewModel(
            service,
            new TestLogger());

        var refresh = viewModel.RefreshAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(refresh.IsCompleted);
        Assert.IsTrue(viewModel.IsBusy);
        Assert.IsTrue(viewModel.IsScanProgressIndeterminate);
        StringAssert.Contains(
            viewModel.ScanProgressDisplay,
            "Discovering running processes");

        completion.SetResult(
            Result<IReadOnlyList<ProcessSummary>>.Success(
                [ProcessSummaryFactory.Create(9, "Completed")]));
        await refresh;

        Assert.IsFalse(viewModel.IsBusy);
        Assert.AreEqual("Completed", viewModel.Processes.Single().ProcessName);
    }

    [TestMethod]
    public async Task ScanReportsKnownProcessCountAndPercentage()
    {
        var progressReported = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ProgressProcessService(
            async (cancellationToken, progress) =>
            {
                progress?.Report(new ProcessScanProgress(0, null));
                progress?.Report(new ProcessScanProgress(2, 4));
                progressReported.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return Result<IReadOnlyList<ProcessSummary>>.Success(
                    [ProcessSummaryFactory.Create(9, "Completed")]);
            });
        using var viewModel = new ProcessExplorerViewModel(
            service,
            new TestLogger());

        var scan = viewModel.RefreshAsync();
        await progressReported.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(viewModel.IsBusy);
        Assert.IsFalse(viewModel.IsScanProgressIndeterminate);
        Assert.AreEqual(2, viewModel.ScannedProcessCount);
        Assert.AreEqual(4, viewModel.TotalProcessCount);
        Assert.AreEqual(50d, viewModel.ScanProgressPercentage);
        StringAssert.Contains(viewModel.ScanProgressDisplay, "2 of 4");

        release.TrySetResult();
        await scan;
    }

    [TestMethod]
    public async Task AutoRefreshUsesTheConfiguredInterval()
    {
        var secondCall = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var service = new DelegateProcessService(
            _ =>
            {
                if (Interlocked.Increment(ref callCount) >= 1)
                {
                    secondCall.TrySetResult();
                }

                return Task.FromResult(
                    Result<IReadOnlyList<ProcessSummary>>.Success(
                        [ProcessSummaryFactory.Create(1, "Auto")]));
            });

        using var viewModel = new ProcessExplorerViewModel(
            service,
            new TestLogger());
        var settings = AppSettings.CreateDefault() with
        {
            ProcessRefreshIntervalMilliseconds = 20,
        };
        await viewModel.InitializeAsync(settings);

        viewModel.IsAutoRefreshEnabled = true;
        await secondCall.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.IsAutoRefreshEnabled = false;

        Assert.IsTrue(service.CallCount >= 1);
    }
}

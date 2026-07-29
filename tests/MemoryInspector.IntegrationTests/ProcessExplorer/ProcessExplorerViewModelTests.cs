using MemoryInspector.Application.Configuration;
using MemoryInspector.Common;
using MemoryInspector.Core.Processes;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.IntegrationTests.ProcessExplorer;

[TestClass]
public sealed class ProcessExplorerViewModelTests
{
    [TestMethod]
    public async Task InitializeLoadsAndFormatsProcessesWithoutBlockingFilters()
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

        completion.SetResult(
            Result<IReadOnlyList<ProcessSummary>>.Success(
                [ProcessSummaryFactory.Create(9, "Completed")]));
        await refresh;

        Assert.IsFalse(viewModel.IsBusy);
        Assert.AreEqual("Completed", viewModel.Processes.Single().ProcessName);
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
                if (Interlocked.Increment(ref callCount) >= 2)
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

        Assert.IsTrue(service.CallCount >= 2);
    }
}

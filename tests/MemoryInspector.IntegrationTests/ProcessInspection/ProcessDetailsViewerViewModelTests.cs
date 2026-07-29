using MemoryInspector.Application.ProcessInspection;
using MemoryInspector.Common;
using MemoryInspector.Core.ProcessInspection;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.IntegrationTests.ProcessExplorer;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.IntegrationTests.ProcessInspection;

[TestClass]
public sealed class ProcessDetailsViewerViewModelTests
{
    private static readonly MonitoringSessionIdentity Identity = new(
        42,
        new DateTimeOffset(
            2026,
            7,
            29,
            8,
            30,
            0,
            TimeSpan.Zero),
        ProcessArchitecture.X64,
        "Target");

    [TestMethod]
    public async Task RefreshLoadsFormatsAndOrdersModulesAndThreads()
    {
        var sessions = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            FromModules(
                Module("z.dll", 0x3000, 2048),
                Module("a.dll", 0x1000, 1024)),
            FromThreads(
                Thread(30),
                Thread(10)),
            sessions);
        await sessions.StartAsync(Identity);

        await viewModel.RefreshAsync();

        Assert.AreEqual(2, viewModel.Modules.Count);
        Assert.AreEqual("a.dll", viewModel.Modules[0].Name);
        Assert.AreEqual(
            "0x0000000000001000",
            viewModel.Modules[0].BaseAddressDisplay);
        Assert.AreEqual("1 KB",
            viewModel.Modules[0].SizeDisplay);
        Assert.AreEqual(10,
            viewModel.Threads[0].ThreadId);
        Assert.AreEqual("00:00:10",
            viewModel.Threads[0].CpuTimeDisplay);
        Assert.AreEqual(
            "2 of 2 modules",
            viewModel.ModuleCountDisplay);
        Assert.AreEqual(
            "2 of 2 threads",
            viewModel.ThreadCountDisplay);
    }

    [TestMethod]
    public async Task SearchAndSortApplyIndependently()
    {
        var sessions = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            FromModules(
                Module("alpha.dll", 0x1000, 100),
                Module("beta.dll", 0x2000, 300)),
            FromThreads(
                Thread(1, "Waiting", priority: 4),
                Thread(2, "Running", priority: 10)),
            sessions);
        await sessions.StartAsync(Identity);
        await viewModel.RefreshAsync();

        viewModel.ModuleSearchText = "beta";
        viewModel.SelectedModuleSort =
            ProcessModuleSortOption.Size;
        viewModel.ModuleSortDescending = true;
        viewModel.ThreadSearchText = "Running";
        viewModel.SelectedThreadSort =
            ProcessThreadSortOption.Priority;
        viewModel.ThreadSortDescending = true;

        Assert.AreEqual(
            "beta.dll",
            viewModel.Modules.Single().Name);
        Assert.AreEqual(
            2,
            viewModel.Threads.Single().ThreadId);
        Assert.AreEqual(
            "1 of 2 modules",
            viewModel.ModuleCountDisplay);
        Assert.AreEqual(
            "1 of 2 threads",
            viewModel.ThreadCountDisplay);
    }

    [TestMethod]
    public async Task ModuleFailureDoesNotHideThreadResults()
    {
        var sessions = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            new DelegateModuleService(
                Result<ProcessModuleQueryResult>.Failure(
                    new Error(
                        ErrorCode.AccessDenied,
                        "Modules denied."))),
            FromThreads(Thread(7)),
            sessions);
        await sessions.StartAsync(Identity);

        await viewModel.RefreshAsync();

        Assert.AreEqual(0, viewModel.Modules.Count);
        Assert.AreEqual(1, viewModel.Threads.Count);
        Assert.IsNotNull(viewModel.ErrorMessage);
        StringAssert.Contains(
            viewModel.ErrorMessage,
            "Modules denied.");
    }

    [TestMethod]
    public async Task SessionStopClearsRowsAndDisablesRefresh()
    {
        var sessions = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            FromModules(Module("a.dll", 0x1000, 100)),
            FromThreads(Thread(1)),
            sessions);
        await sessions.StartAsync(Identity);
        await viewModel.RefreshAsync();

        await sessions.StopAsync();

        Assert.AreEqual(0, viewModel.Modules.Count);
        Assert.AreEqual(0, viewModel.Threads.Count);
        Assert.IsFalse(viewModel.IsSessionConnected);
        Assert.IsFalse(
            viewModel.RefreshCommand.CanExecute(null));
    }

    private static ProcessDetailsViewerViewModel CreateViewModel(
        IProcessModuleService modules,
        IProcessThreadService threads,
        RecordingMonitoringSessionService sessions)
    {
        return new ProcessDetailsViewerViewModel(
            modules,
            threads,
            sessions,
            new TestLogger());
    }

    private static DelegateModuleService FromModules(
        params ProcessModuleInfo[] modules)
    {
        return new DelegateModuleService(
            Result<ProcessModuleQueryResult>.Success(
                new ProcessModuleQueryResult(modules)));
    }

    private static DelegateThreadService FromThreads(
        params ProcessThreadInfo[] threads)
    {
        return new DelegateThreadService(
            Result<ProcessThreadQueryResult>.Success(
                new ProcessThreadQueryResult(threads)));
    }

    private static ProcessModuleInfo Module(
        string name,
        ulong address,
        ulong size)
    {
        return new ProcessModuleInfo(
            name,
            address,
            size,
            $@"C:\Modules\{name}",
            "1.0.0.0");
    }

    private static ProcessThreadInfo Thread(
        int id,
        string state = "Running",
        int priority = 8)
    {
        return new ProcessThreadInfo(
            id,
            state,
            priority,
            new DateTimeOffset(
                2026,
                7,
                29,
                8,
                30,
                0,
                TimeSpan.Zero),
            TimeSpan.FromSeconds(id));
    }

    private sealed class DelegateModuleService(
        Result<ProcessModuleQueryResult> result) :
        IProcessModuleService
    {
        public Task<Result<ProcessModuleQueryResult>>
            GetModulesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class DelegateThreadService(
        Result<ProcessThreadQueryResult> result) :
        IProcessThreadService
    {
        public Task<Result<ProcessThreadQueryResult>>
            GetThreadsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }
}

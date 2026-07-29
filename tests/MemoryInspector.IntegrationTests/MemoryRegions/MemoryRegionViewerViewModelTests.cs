using MemoryInspector.Application.Memory;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.IntegrationTests.ProcessExplorer;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.IntegrationTests.MemoryRegions;

[TestClass]
public sealed class MemoryRegionViewerViewModelTests
{
    private static readonly MonitoringSessionIdentity Identity = new(
        42,
        new DateTimeOffset(2026, 7, 29, 8, 30, 0, TimeSpan.Zero),
        ProcessArchitecture.X64,
        "Target");

    [TestMethod]
    public async Task RefreshLoadsFormatsAndOrdersAllRegions()
    {
        var memoryService = FromRegions(
            CreateRegion(0x3000, 0x1000),
            CreateRegion(0x1000, 0x2000));
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            memoryService,
            monitoringService);
        await monitoringService.StartAsync(Identity);

        await viewModel.RefreshAsync();

        Assert.AreEqual(2, viewModel.Regions.Count);
        Assert.AreEqual(0x1000UL, viewModel.Regions[0].BaseAddress);
        Assert.AreEqual(
            "0x0000000000001000",
            viewModel.Regions[0].BaseAddressDisplay);
        Assert.AreEqual("8 KB", viewModel.Regions[0].SizeDisplay);
        Assert.AreEqual("2 of 2 regions", viewModel.RegionCountDisplay);
        Assert.IsNotNull(viewModel.LastRefreshedAt);
    }

    [TestMethod]
    public async Task AddressAndAttributeFiltersApplyImmediately()
    {
        var memoryService = FromRegions(
            CreateRegion(
                0x1000,
                0x1000,
                MemoryRegionType.Private,
                MemoryProtection.ReadWrite),
            CreateRegion(
                0x2000,
                0x1000,
                MemoryRegionType.Image,
                MemoryProtection.ExecuteRead),
            CreateRegion(
                0x3000,
                0x1000,
                MemoryRegionType.Private,
                MemoryProtection.ReadWrite |
                MemoryProtection.Guard));
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            memoryService,
            monitoringService);
        await monitoringService.StartAsync(Identity);
        await viewModel.RefreshAsync();

        viewModel.AddressSearchText = "0x2800";
        Assert.AreEqual(0x2000UL, viewModel.Regions.Single().BaseAddress);

        viewModel.AddressSearchText = string.Empty;
        viewModel.SelectedTypeFilter = MemoryRegionTypeFilter.Private;
        viewModel.SelectedAccessFilter = MemoryRegionAccessFilter.Writable;
        Assert.AreEqual(0x1000UL, viewModel.Regions.Single().BaseAddress);

        viewModel.SelectedAccessFilter = MemoryRegionAccessFilter.All;
        viewModel.SelectedProtectionFilter =
            MemoryRegionProtectionFilter.Guard;
        Assert.AreEqual(0x3000UL, viewModel.Regions.Single().BaseAddress);
    }

    [TestMethod]
    public async Task InvalidAddressShowsFilterMessageAndNoRows()
    {
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            FromRegions(CreateRegion(0x1000, 0x1000)),
            monitoringService);
        await monitoringService.StartAsync(Identity);
        await viewModel.RefreshAsync();

        viewModel.AddressSearchText = "not-an-address";

        Assert.AreEqual(0, viewModel.Regions.Count);
        Assert.IsNotNull(viewModel.FilterMessage);
    }

    [TestMethod]
    public async Task SizeSortSupportsBothDirections()
    {
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            FromRegions(
                CreateRegion(0x1000, 0x3000),
                CreateRegion(0x4000, 0x1000),
                CreateRegion(0x5000, 0x2000)),
            monitoringService);
        await monitoringService.StartAsync(Identity);
        await viewModel.RefreshAsync();

        viewModel.SelectedSortOption = MemoryRegionSortOption.Size;
        CollectionAssert.AreEqual(
            new ulong[] { 0x1000, 0x2000, 0x3000 },
            viewModel.Regions.Select(region => region.Size).ToArray());

        viewModel.SortDescending = true;
        CollectionAssert.AreEqual(
            new ulong[] { 0x3000, 0x2000, 0x1000 },
            viewModel.Regions.Select(region => region.Size).ToArray());
    }

    [TestMethod]
    public async Task RefreshPreservesSelectedRegionIdentity()
    {
        var responses = new Queue<MemoryRegionQueryResult>(
        [
            CreateQueryResult(
                CreateRegion(0x1000, 0x1000),
                CreateRegion(0x2000, 0x1000)),
            CreateQueryResult(
                CreateRegion(0x1000, 0x1000),
                CreateRegion(0x2000, 0x1000)),
        ]);
        var memoryService = new DelegateMemoryRegionService(
            _ => Task.FromResult(
                Result<MemoryRegionQueryResult>.Success(
                    responses.Dequeue())));
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            memoryService,
            monitoringService);
        await monitoringService.StartAsync(Identity);
        await viewModel.RefreshAsync();
        viewModel.SelectedRegion = viewModel.Regions[1];

        await viewModel.RefreshAsync();

        Assert.IsNotNull(viewModel.SelectedRegion);
        Assert.AreEqual(
            0x2000UL,
            viewModel.SelectedRegion.BaseAddress);
    }

    [TestMethod]
    public async Task RefreshRemainsAsynchronousWhileProviderIsRunning()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<
            Result<MemoryRegionQueryResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var memoryService = new DelegateMemoryRegionService(
            cancellationToken =>
            {
                started.TrySetResult();
                cancellationToken.Register(() =>
                    completion.TrySetCanceled(cancellationToken));
                return completion.Task;
            });
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            memoryService,
            monitoringService);
        await monitoringService.StartAsync(Identity);

        var refresh = viewModel.RefreshAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(refresh.IsCompleted);
        Assert.IsTrue(viewModel.IsBusy);

        completion.SetResult(
            Result<MemoryRegionQueryResult>.Success(
                CreateQueryResult(
                    CreateRegion(0x1000, 0x1000))));
        await refresh;

        Assert.IsFalse(viewModel.IsBusy);
        Assert.AreEqual(1, viewModel.Regions.Count);
    }

    [TestMethod]
    public async Task PartialResultDisplaysWarningWithoutDiscardingRows()
    {
        var warning = new Error(
            ErrorCode.NativeApi,
            "Enumeration stopped early.");
        var queryResult = new MemoryRegionQueryResult(
            [CreateRegion(0x1000, 0x1000)],
            [warning]);
        var memoryService = new DelegateMemoryRegionService(
            _ => Task.FromResult(
                Result<MemoryRegionQueryResult>.Success(queryResult)));
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            memoryService,
            monitoringService);
        await monitoringService.StartAsync(Identity);

        await viewModel.RefreshAsync();

        Assert.AreEqual(1, viewModel.Regions.Count);
        Assert.IsTrue(
            viewModel.StatusMessage.Contains(
                "partial",
                StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(
            "Enumeration stopped early.",
            viewModel.WarningMessage);
    }

    [TestMethod]
    public async Task SessionStopClearsRegionsAndDisablesRefresh()
    {
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            FromRegions(CreateRegion(0x1000, 0x1000)),
            monitoringService);
        await monitoringService.StartAsync(Identity);
        await viewModel.RefreshAsync();

        await monitoringService.StopAsync();

        Assert.AreEqual(0, viewModel.Regions.Count);
        Assert.IsNull(viewModel.SelectedRegion);
        Assert.IsFalse(viewModel.RefreshCommand.CanExecute(null));
        Assert.IsFalse(viewModel.IsSessionConnected);
    }

    [TestMethod]
    public async Task LargeRegionSetReusesRowsDuringFiltering()
    {
        var source = Enumerable
            .Range(0, 10_000)
            .Select(index =>
                CreateRegion(
                    (ulong)index * 0x1000,
                    0x1000))
            .ToArray();
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            FromRegions(source),
            monitoringService);
        await monitoringService.StartAsync(Identity);
        await viewModel.RefreshAsync();
        var expected = viewModel.Regions[5_000];

        viewModel.AddressSearchText = "1388000";

        Assert.AreEqual(1, viewModel.Regions.Count);
        Assert.AreSame(expected, viewModel.Regions.Single());
    }

    [TestMethod]
    public async Task SelectedRegionCanOpenHexViewerWithBounds()
    {
        var monitoringService = new RecordingMonitoringSessionService();
        using var viewModel = CreateViewModel(
            FromRegions(CreateRegion(0x4000, 0x2000)),
            monitoringService);
        await monitoringService.StartAsync(Identity);
        await viewModel.RefreshAsync();
        viewModel.SelectedRegion = viewModel.Regions.Single();
        HexViewerRequestedEventArgs? request = null;
        viewModel.OpenHexRequested +=
            (_, eventArgs) => request = eventArgs;

        viewModel.OpenHexCommand.Execute(null);

        Assert.IsNotNull(request);
        Assert.AreEqual(0x4000UL, request.Address);
        Assert.AreSame(
            viewModel.SelectedRegion.Region,
            request.Region);
    }

    private static MemoryRegionViewerViewModel CreateViewModel(
        IMemoryRegionService memoryService,
        RecordingMonitoringSessionService monitoringService)
    {
        return new MemoryRegionViewerViewModel(
            memoryService,
            monitoringService,
            new TestLogger());
    }

    private static DelegateMemoryRegionService FromRegions(
        params MemoryRegion[] regions)
    {
        return new DelegateMemoryRegionService(
            _ => Task.FromResult(
                Result<MemoryRegionQueryResult>.Success(
                    CreateQueryResult(regions))));
    }

    private static MemoryRegionQueryResult CreateQueryResult(
        params MemoryRegion[] regions)
    {
        return new MemoryRegionQueryResult(regions);
    }

    private static MemoryRegion CreateRegion(
        ulong baseAddress,
        ulong size,
        MemoryRegionType type = MemoryRegionType.Private,
        MemoryProtection protection = MemoryProtection.ReadWrite)
    {
        return new MemoryRegion(
            baseAddress,
            size,
            baseAddress,
            MemoryRegionState.Committed,
            type,
            protection);
    }

    private sealed class DelegateMemoryRegionService(
        Func<
            CancellationToken,
            Task<Result<MemoryRegionQueryResult>>> getRegions)
        : IMemoryRegionService
    {
        public int CallCount { get; private set; }

        public Task<Result<MemoryRegionQueryResult>> GetRegionsAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return getRegions(cancellationToken);
        }
    }
}

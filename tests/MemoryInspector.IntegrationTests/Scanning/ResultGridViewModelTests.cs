using System.Buffers.Binary;
using System.Diagnostics;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Scanning.Results;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.IntegrationTests.ProcessExplorer;
using MemoryInspector.Wpf.Services;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.IntegrationTests.Scanning;

[TestClass]
public sealed class ResultGridViewModelTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MillionCandidateSnapshotKeepsOnlyCurrentPageRows()
    {
        var service = new DelegateResultGridService(
            (snapshot, pageNumber, pageSize, _) =>
                Task.FromResult(CreatePage(
                    snapshot,
                    pageNumber,
                    pageSize,
                    totalCount: 1_000_000)));
        using var viewModel = CreateViewModel(service);
        await viewModel.InitializeAsync(
            AppSettings.CreateDefault());
        var snapshot = ResultGridServiceTests.CreateSnapshot(
            recordCount: 1_000_000);

        await viewModel.ShowSnapshotAsync(snapshot);

        Assert.AreEqual(1_000, viewModel.Rows.Count);
        Assert.AreEqual(1_000L, viewModel.TotalPages);
        Assert.AreEqual(1L, viewModel.PageNumber);
        Assert.IsTrue(viewModel.CanGoToNextPage);

        await viewModel.LoadPageAsync(1_000);

        Assert.AreEqual(1_000, viewModel.Rows.Count);
        Assert.AreEqual(1_000L, viewModel.PageNumber);
        Assert.IsFalse(viewModel.CanGoToNextPage);
        Assert.AreEqual(2, service.CallCount);
        Assert.IsTrue(service.RequestedPageSizes.All(
            pageSize => pageSize == 1_000));
    }

    [TestMethod]
    public async Task NewPageRequestCancelsPreviousLazyLoad()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new DelegateResultGridService(
            async (snapshot, pageNumber, pageSize, cancellationToken) =>
            {
                if (pageNumber == 1)
                {
                    firstStarted.TrySetResult();

                    try
                    {
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken);
                    }
                    catch (OperationCanceledException exception)
                    {
                        firstCancelled.TrySetResult();
                        return Result<
                            PagedResult<ResultGridItem>>.Failure(
                            new Error(
                                ErrorCode.Cancelled,
                                "First page was cancelled.",
                                exception));
                    }
                }

                return CreatePage(
                    snapshot,
                    pageNumber,
                    pageSize,
                    totalCount: 3_000);
            });
        using var viewModel = CreateViewModel(service);
        await viewModel.InitializeAsync(
            AppSettings.CreateDefault());
        var snapshot = ResultGridServiceTests.CreateSnapshot(
            recordCount: 3_000);

        var firstLoad = viewModel.ShowSnapshotAsync(snapshot);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondLoad = viewModel.LoadPageAsync(2);
        await Task.WhenAll(firstLoad, secondLoad)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(firstCancelled.Task.IsCompleted);
        Assert.AreEqual(2L, viewModel.PageNumber);
        Assert.AreEqual(1_000, viewModel.Rows.Count);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(30_000)]
    public async Task RapidPageRequestsCompleteWithLatestPageVisible()
    {
        const int requestCount = 100;
        var service = new DelegateResultGridService(
            async (snapshot, pageNumber, pageSize, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(15),
                        cancellationToken);
                }
                catch (OperationCanceledException exception)
                {
                    return Result<PagedResult<ResultGridItem>>.Failure(
                        new Error(
                            ErrorCode.Cancelled,
                            "Superseded page request was cancelled.",
                            exception));
                }

                return CreatePage(
                    snapshot,
                    pageNumber,
                    pageSize,
                    totalCount: 100_000);
            });
        using var viewModel = CreateViewModel(service);
        await viewModel.InitializeAsync(
            AppSettings.CreateDefault());
        await viewModel.ShowSnapshotAsync(
            ResultGridServiceTests.CreateSnapshot(
                recordCount: 100_000));
        var timer = Stopwatch.StartNew();

        var requests = Enumerable.Range(1, requestCount)
            .Select(pageNumber =>
                viewModel.LoadPageAsync(pageNumber))
            .ToArray();
        await Task.WhenAll(requests);

        timer.Stop();
        var averageRequestLatencyMilliseconds =
            timer.Elapsed.TotalMilliseconds / requestCount;
        TestContext.WriteLine(
            $"METRIC rapid_page_requests={requestCount}");
        Console.WriteLine(
            $"METRIC rapid_page_requests={requestCount}");
        TestContext.WriteLine(
            $"METRIC rapid_page_total_milliseconds=" +
            $"{timer.Elapsed.TotalMilliseconds:F1}");
        Console.WriteLine(
            $"METRIC rapid_page_total_milliseconds=" +
            $"{timer.Elapsed.TotalMilliseconds:F1}");
        TestContext.WriteLine(
            $"METRIC rapid_page_average_milliseconds=" +
            $"{averageRequestLatencyMilliseconds:F2}");
        Console.WriteLine(
            $"METRIC rapid_page_average_milliseconds=" +
            $"{averageRequestLatencyMilliseconds:F2}");

        Assert.AreEqual(
            (long)requestCount,
            viewModel.PageNumber);
        Assert.AreEqual(1_000, viewModel.Rows.Count);
        Assert.IsFalse(viewModel.IsBusy);
        Assert.AreEqual(
            requestCount + 1,
            service.CallCount);
        Assert.IsTrue(
            timer.Elapsed < TimeSpan.FromSeconds(5),
            $"Rapid paging took {timer.Elapsed.TotalSeconds:F2}s.");
    }

    [TestMethod]
    public async Task SortIsLimitedToCurrentPage()
    {
        var service = new DelegateResultGridService(
            (snapshot, pageNumber, pageSize, _) =>
                Task.FromResult(CreateValuePage(
                    snapshot,
                    pageNumber,
                    pageSize,
                    [30, 10, 20],
                    totalCount: 10_000)));
        using var viewModel = CreateViewModel(service);
        await viewModel.InitializeAsync(
            AppSettings.CreateDefault());
        await viewModel.ShowSnapshotAsync(
            ResultGridServiceTests.CreateSnapshot(
                recordCount: 10_000));

        viewModel.SelectedSortOption =
            ResultGridSortOption.Value;
        CollectionAssert.AreEqual(
            new[] { "10", "20", "30" },
            viewModel.Rows
                .Select(row => row.ValueDisplay)
                .ToArray());

        viewModel.SortDescending = true;
        CollectionAssert.AreEqual(
            new[] { "30", "20", "10" },
            viewModel.Rows
                .Select(row => row.ValueDisplay)
                .ToArray());
        Assert.AreEqual(3, viewModel.Rows.Count);
    }

    [TestMethod]
    public async Task SelectedAddressSupportsCopyWatchAndSaveActions()
    {
        var clipboard = new RecordingClipboardService();
        var service = new DelegateResultGridService(
            (snapshot, pageNumber, pageSize, _) =>
                Task.FromResult(CreateValuePage(
                    snapshot,
                    pageNumber,
                    pageSize,
                    [7],
                    totalCount: 1)));
        using var viewModel = CreateViewModel(
            service,
            clipboard);
        await viewModel.InitializeAsync(
            AppSettings.CreateDefault());
        await viewModel.ShowSnapshotAsync(
            ResultGridServiceTests.CreateSnapshot(
                recordCount: 1));
        viewModel.SelectedRow = viewModel.Rows[0];
        ResultGridRowViewModel? watched = null;
        ResultGridRowViewModel? saved = null;
        MemoryEditRequestedEventArgs? edited = null;
        HexViewerRequestedEventArgs? openedInHex = null;
        viewModel.AddToWatchRequested +=
            (_, eventArgs) => watched = eventArgs.Row;
        viewModel.SaveAddressRequested +=
            (_, eventArgs) => saved = eventArgs.Row;
        viewModel.EditValueRequested +=
            (_, eventArgs) => edited = eventArgs;
        viewModel.OpenHexRequested +=
            (_, eventArgs) => openedInHex = eventArgs;

        viewModel.CopyAddressCommand.Execute(null);
        viewModel.AddToWatchCommand.Execute(null);
        viewModel.SaveAddressCommand.Execute(null);
        viewModel.EditValueCommand.Execute(null);
        viewModel.OpenHexCommand.Execute(null);

        Assert.AreEqual(
            viewModel.SelectedRow.AddressDisplay,
            clipboard.Text);
        Assert.AreSame(viewModel.SelectedRow, watched);
        Assert.AreSame(viewModel.SelectedRow, saved);
        Assert.AreEqual(
            viewModel.SelectedRow.Address,
            edited!.Address);
        Assert.AreEqual(
            MemoryInspector.Core.Memory.Editing.MemoryWriteSource.ScanResult,
            edited.Source);
        Assert.AreEqual(
            viewModel.SelectedRow.Address,
            openedInHex!.Address);
        Assert.IsNull(openedInHex.Region);
    }

    private static ResultGridViewModel CreateViewModel(
        IResultGridService service,
        IClipboardService? clipboard = null)
    {
        return new ResultGridViewModel(
            service,
            clipboard ?? new RecordingClipboardService(),
            new TestLogger());
    }

    private static Result<PagedResult<ResultGridItem>> CreatePage(
        SnapshotDescriptor snapshot,
        long pageNumber,
        int pageSize,
        long totalCount)
    {
        var remaining = Math.Max(
            0,
            totalCount -
            (pageNumber - 1) * pageSize);
        var count = (int)Math.Min(pageSize, remaining);
        var values = Enumerable.Range(0, count).ToArray();
        return CreateValuePage(
            snapshot,
            pageNumber,
            pageSize,
            values,
            totalCount);
    }

    private static Result<PagedResult<ResultGridItem>>
        CreateValuePage(
            SnapshotDescriptor snapshot,
            long pageNumber,
            int pageSize,
            IReadOnlyList<int> values,
            long totalCount)
    {
        var start = checked(
            (pageNumber - 1) * pageSize);
        var items = values.Select((value, index) =>
        {
            var bytes = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes,
                value);
            return new ResultGridItem(
                checked((ulong)(0x1_000 + start + index)),
                snapshot.ValueType,
                bytes,
                ResultReadStatus.Available);
        }).ToArray();

        return Result<PagedResult<ResultGridItem>>.Success(
            new PagedResult<ResultGridItem>(
                items,
                pageNumber,
                pageSize,
                totalCount));
    }

    private sealed class DelegateResultGridService(
        Func<
            SnapshotDescriptor,
            long,
            int,
            CancellationToken,
            Task<Result<PagedResult<ResultGridItem>>>> loadPage)
        : IResultGridService
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public List<int> RequestedPageSizes { get; } = [];

        public Task<Result<PagedResult<ResultGridItem>>>
            LoadPageAsync(
                SnapshotDescriptor snapshot,
                long pageNumber,
                int pageSize,
                CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);

            lock (RequestedPageSizes)
            {
                RequestedPageSizes.Add(pageSize);
            }

            return loadPage(
                snapshot,
                pageNumber,
                pageSize,
                cancellationToken);
        }
    }

    private sealed class RecordingClipboardService :
        IClipboardService
    {
        public string? Text { get; private set; }

        public Result SetText(string text)
        {
            Text = text;
            return Result.Success();
        }
    }
}

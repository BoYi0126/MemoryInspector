using MemoryInspector.Application.Memory;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.IntegrationTests.ProcessExplorer;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.IntegrationTests.Memory;

[TestClass]
public sealed class HexViewerViewModelTests
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
    public async Task RegionOpenReadsOnlyFixedWindowAndFormatsRows()
    {
        var reader = new DelegateMemoryReaderService(
            (address, length) => Success(
                address,
                length,
                Enumerable.Range(0, length)
                    .Select(index => (byte)(index % 256))
                    .ToArray()));
        var sessions = await CreateConnectedSessionsAsync();
        using var viewModel = CreateViewModel(reader, sessions);
        var region = Region(
            0x1000,
            HexViewerViewModel.WindowSizeBytes * 3UL);

        await viewModel.OpenRegionAsync(region);

        Assert.AreEqual(1, reader.ReadCallCount);
        Assert.AreEqual(
            HexViewerViewModel.WindowSizeBytes,
            reader.LastLength);
        Assert.AreEqual(256, viewModel.Rows.Count);
        Assert.AreEqual(
            "0x0000000000001000",
            viewModel.Rows[0].AddressDisplay);
        Assert.AreEqual("+0x00000000",
            viewModel.Rows[0].OffsetDisplay);
        StringAssert.StartsWith(
            viewModel.Rows[0].HexDisplay,
            "00 01 02 03");
        Assert.AreEqual(
            "................",
            viewModel.Rows[0].AsciiDisplay);
        Assert.AreEqual("Page 1 of 3", viewModel.PageDisplay);
    }

    [TestMethod]
    public async Task PartialReadKeepsRowsAndMarksUnreadableBytes()
    {
        var warning = new Error(
            ErrorCode.NativeApi,
            "Read stopped early.");
        var reader = new DelegateMemoryReaderService(
            (address, length) =>
                Result<MemoryReadResult>.Success(
                    new MemoryReadResult(
                        new MemoryReadRequest(address, length),
                        new byte[] { 0x41, 0x42, 0x1F },
                        [warning])));
        var sessions = await CreateConnectedSessionsAsync();
        using var viewModel = CreateViewModel(reader, sessions);

        await viewModel.OpenRegionAsync(Region(0x2000, 32));

        Assert.AreEqual(2, viewModel.Rows.Count);
        Assert.IsTrue(viewModel.Rows[0].HasUnreadableBytes);
        StringAssert.Contains(
            viewModel.Rows[0].HexDisplay,
            "41 42 1F ??");
        StringAssert.StartsWith(
            viewModel.Rows[0].AsciiDisplay,
            "AB.·");
        Assert.IsNotNull(viewModel.WarningMessage);
        StringAssert.Contains(
            viewModel.WarningMessage,
            "Read stopped early.");
    }

    [TestMethod]
    public async Task SearchFindsPatternAcrossRowsAndRejectsBadHex()
    {
        var data = Enumerable.Repeat((byte)0x00, 64).ToArray();
        data[15] = 0xDE;
        data[16] = 0xAD;
        data[17] = 0xBE;
        data[18] = 0xEF;
        var reader = new DelegateMemoryReaderService(
            (address, length) => Success(address, length, data));
        var sessions = await CreateConnectedSessionsAsync();
        using var viewModel = CreateViewModel(reader, sessions);
        await viewModel.OpenRegionAsync(Region(0x3000, 64));

        viewModel.SearchText = "DE AD-BE,EF";
        viewModel.SearchBytes();

        Assert.AreEqual(
            "Match at 0x000000000000300F",
            viewModel.SearchMatchDisplay);
        Assert.AreEqual(
            2,
            viewModel.Rows.Count(row => row.IsSearchMatch));
        Assert.IsNotNull(viewModel.SelectedRow);

        viewModel.SearchText = "ABC";
        viewModel.SearchBytes();

        Assert.IsNotNull(viewModel.InputMessage);
        Assert.AreEqual(
            "No active match",
            viewModel.SearchMatchDisplay);
    }

    [TestMethod]
    public async Task RegionPagingClampsFinalPageAndJumpValidatesBounds()
    {
        var reader = new DelegateMemoryReaderService(
            (address, length) => Success(
                address,
                length,
                new byte[length]));
        var sessions = await CreateConnectedSessionsAsync();
        using var viewModel = CreateViewModel(reader, sessions);
        var region = Region(
            0x1005,
            (HexViewerViewModel.WindowSizeBytes * 2UL) + 1);
        await viewModel.OpenRegionAsync(
            region,
            0x2005);

        Assert.AreEqual("Page 2 of 3", viewModel.PageDisplay);
        Assert.IsTrue(viewModel.CanGoToPreviousPage);
        Assert.IsTrue(viewModel.CanGoToNextPage);

        await viewModel.GoToNextPageAsync();

        Assert.AreEqual(1, reader.LastLength);
        Assert.AreEqual("Page 3 of 3", viewModel.PageDisplay);
        Assert.IsFalse(viewModel.CanGoToNextPage);

        viewModel.AddressText = "0x5000";
        await viewModel.JumpAsync();

        Assert.IsNotNull(viewModel.InputMessage);
        StringAssert.Contains(
            viewModel.InputMessage,
            "outside");
    }

    [TestMethod]
    public async Task ReadFailureShowsUnreadableWindowWithoutLosingShape()
    {
        var reader = new DelegateMemoryReaderService(
            (_, _) => Result<MemoryReadResult>.Failure(
                new Error(
                    ErrorCode.AccessDenied,
                    "Memory access denied.")));
        var sessions = await CreateConnectedSessionsAsync();
        using var viewModel = CreateViewModel(reader, sessions);

        await viewModel.OpenAddressAsync(0x4567);

        Assert.AreEqual(256, viewModel.Rows.Count);
        Assert.IsTrue(viewModel.Rows.All(row =>
            row.HasUnreadableBytes));
        Assert.IsTrue(viewModel.Rows[0].HexDisplay.StartsWith(
            "?? ??",
            StringComparison.Ordinal));
        Assert.IsNotNull(viewModel.WarningMessage);
    }

    [TestMethod]
    public async Task SessionStopCancelsAndClearsWindow()
    {
        var reader = new DelegateMemoryReaderService(
            (address, length) => Success(
                address,
                length,
                new byte[length]));
        var sessions = await CreateConnectedSessionsAsync();
        using var viewModel = CreateViewModel(reader, sessions);
        await viewModel.OpenAddressAsync(0x4000);

        await sessions.StopAsync();

        Assert.AreEqual(0, viewModel.Rows.Count);
        Assert.IsFalse(viewModel.HasWindow);
        Assert.IsFalse(viewModel.IsSessionConnected);
        Assert.IsFalse(
            viewModel.RefreshCommand.CanExecute(null));
    }

    private static HexViewerViewModel CreateViewModel(
        IMemoryReaderService reader,
        RecordingMonitoringSessionService sessions)
    {
        return new HexViewerViewModel(
            reader,
            sessions,
            new TestLogger());
    }

    private static async Task<RecordingMonitoringSessionService>
        CreateConnectedSessionsAsync()
    {
        var sessions = new RecordingMonitoringSessionService();
        await sessions.StartAsync(Identity);
        return sessions;
    }

    private static MemoryRegion Region(
        ulong address,
        ulong size)
    {
        return new MemoryRegion(
            address,
            size,
            address,
            MemoryRegionState.Committed,
            MemoryRegionType.Private,
            MemoryProtection.ReadWrite);
    }

    private static Result<MemoryReadResult> Success(
        ulong address,
        int length,
        byte[] data)
    {
        return Result<MemoryReadResult>.Success(
            new MemoryReadResult(
                new MemoryReadRequest(address, length),
                data.AsSpan(0, Math.Min(data.Length, length))));
    }

    private sealed class DelegateMemoryReaderService(
        Func<ulong, int, Result<MemoryReadResult>> read)
        : IMemoryReaderService
    {
        public int ReadCallCount { get; private set; }

        public ulong LastAddress { get; private set; }

        public int LastLength { get; private set; }

        public Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ReadCallCount++;
            LastAddress = address;
            LastLength = length;
            return Task.FromResult(read(address, length));
        }

        public Task<Result<T>> TryReadAsync<T>(
            ulong address,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            throw new NotSupportedException();
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            IEnumerable<MemoryReadRequest> requests,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}

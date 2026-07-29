using System.Buffers.Binary;
using System.Diagnostics;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning.Results;
using MemoryInspector.Application.Watch;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.IntegrationTests.ProcessExplorer;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.IntegrationTests.Watch;

[TestClass]
public sealed class WatchServiceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task BatchRefreshUpdatesReadableEntriesAndIsolatesFailure()
    {
        var sessions = new MutableSessionService();
        var reader = new DelegateMemoryReaderService
        {
            Batch = (requests, _) =>
            {
                var items = requests
                    .Select((request, index) =>
                        index == 1
                            ? new MemoryBatchReadItem(
                                request,
                                Result<MemoryReadResult>.Failure(
                                    new Error(
                                        ErrorCode.NativeApi,
                                        "Address is unreadable.")))
                            : SuccessItem(
                                request,
                                BitConverter.GetBytes(
                                    index == 0 ? 10 : 30)))
                    .ToArray();
                return Task.FromResult(
                    Result<MemoryBatchReadResult>.Success(
                        new MemoryBatchReadResult(items)));
            },
        };
        using var service = new WatchService(
            reader,
            sessions,
            TimeProvider.System);
        _ = service.Add(0x1000, ScanValueType.Int32);
        _ = service.Add(0x2000, ScanValueType.Int32);
        _ = service.Add(0x3000, ScanValueType.Int32);

        var result = await service.RefreshAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, reader.BatchCallCount);
        Assert.AreEqual(3, reader.LastRequests.Count);
        Assert.AreEqual(2, result.Value.AvailableCount);
        Assert.AreEqual(1, result.Value.UnreadableCount);
        Assert.AreEqual(
            WatchReadStatus.Available,
            service.Entries[0].Status);
        Assert.AreEqual(
            WatchReadStatus.Unreadable,
            service.Entries[1].Status);
        Assert.AreEqual(
            WatchReadStatus.Available,
            service.Entries[2].Status);
        Assert.AreEqual(
            10,
            BitConverter.ToInt32(
                service.Entries[0].CurrentValue!.Value.ToArray()));
        Assert.IsNull(service.Entries[1].CurrentValue);
    }

    [TestMethod]
    public async Task ConsecutiveRefreshPreservesPreviousValueAndComputesDelta()
    {
        var sessions = new MutableSessionService();
        var currentValue = 10;
        var reader = new DelegateMemoryReaderService
        {
            Batch = (requests, _) => BatchSuccess(
                requests,
                _ => BitConverter.GetBytes(currentValue)),
        };
        using var service = new WatchService(
            reader,
            sessions,
            TimeProvider.System);
        _ = service.Add(0x1000, ScanValueType.Int32);
        _ = await service.RefreshAsync();
        currentValue = 15;

        _ = await service.RefreshAsync();

        var entry = service.Entries.Single();
        var row = new WatchEntryRowViewModel(entry);
        Assert.AreEqual(
            10,
            BitConverter.ToInt32(
                entry.PreviousValue!.Value.ToArray()));
        Assert.AreEqual(
            15,
            BitConverter.ToInt32(
                entry.CurrentValue!.Value.ToArray()));
        Assert.AreEqual("5", row.DeltaDisplay);
        Assert.AreEqual(2, reader.BatchCallCount);
    }

    [TestMethod]
    public async Task PausePreventsReadsAndResumeRestoresPendingState()
    {
        var sessions = new MutableSessionService();
        var reader = new DelegateMemoryReaderService
        {
            Batch = (requests, _) => BatchSuccess(
                requests,
                request => new byte[request.Length]),
        };
        using var service = new WatchService(
            reader,
            sessions,
            TimeProvider.System);
        _ = service.Add(0x1000, ScanValueType.Int32);

        var pause = service.SetPaused(true);
        var pausedRefresh = await service.RefreshAsync();

        Assert.IsTrue(pause.IsSuccess);
        Assert.IsTrue(service.IsPaused);
        Assert.IsFalse(service.CanRefresh);
        Assert.AreEqual(
            WatchReadStatus.Paused,
            service.Entries.Single().Status);
        Assert.IsTrue(pausedRefresh.IsFailure);
        Assert.AreEqual(0, reader.BatchCallCount);

        var resume = service.SetPaused(false);

        Assert.IsTrue(resume.IsSuccess);
        Assert.IsFalse(service.IsPaused);
        Assert.IsTrue(service.CanRefresh);
        Assert.AreEqual(
            WatchReadStatus.Pending,
            service.Entries.Single().Status);
    }

    [TestMethod]
    public async Task TargetExitMarksEntriesUnavailableAndStopsFurtherRefresh()
    {
        var sessions = new MutableSessionService();
        var reader = new DelegateMemoryReaderService
        {
            Batch = (requests, _) => BatchSuccess(
                requests,
                request => new byte[request.Length]),
        };
        using var service = new WatchService(
            reader,
            sessions,
            TimeProvider.System);
        _ = service.Add(0x1000, ScanValueType.Int32);

        sessions.Transition(
            MonitoringSessionState.TargetExited,
            "Target process exited.");
        var refresh = await service.RefreshAsync();

        Assert.IsFalse(service.CanRefresh);
        Assert.AreEqual(
            WatchReadStatus.TargetUnavailable,
            service.Entries.Single().Status);
        Assert.AreEqual(
            "Target process exited.",
            service.Entries.Single().StatusMessage);
        Assert.IsTrue(refresh.IsFailure);
        Assert.AreEqual(0, reader.BatchCallCount);
    }

    [TestMethod]
    public async Task ChangingTypeClearsValuesAndChangesReadSize()
    {
        var sessions = new MutableSessionService();
        var reader = new DelegateMemoryReaderService
        {
            Batch = (requests, _) => BatchSuccess(
                requests,
                request => new byte[request.Length]),
        };
        using var service = new WatchService(
            reader,
            sessions,
            TimeProvider.System);
        var added = service.Add(0x1000, ScanValueType.Int32);
        _ = await service.RefreshAsync();

        var changed = service.ChangeType(
            added.Value.Key,
            ScanValueType.UInt64);
        _ = await service.RefreshAsync();

        Assert.IsTrue(changed.IsSuccess);
        Assert.IsNull(changed.Value.PreviousValue);
        Assert.IsNull(changed.Value.CurrentValue);
        Assert.AreEqual(
            ScanValueType.UInt64,
            service.Entries.Single().ValueType);
        Assert.AreEqual(8, reader.LastRequests.Single().Length);
    }

    [TestMethod]
    public void RemovingLastEntryAllowsBindingToANewSession()
    {
        var sessions = new MutableSessionService();
        using var service = new WatchService(
            new DelegateMemoryReaderService(),
            sessions,
            TimeProvider.System);
        var first = service.Add(0x1000, ScanValueType.Int32);
        var firstSession = sessions.CurrentSession!.SessionId;

        _ = service.Remove(first.Value.Key);
        sessions.ConnectNewSession();
        var second = service.Add(0x2000, ScanValueType.Int32);

        Assert.IsTrue(second.IsSuccess);
        Assert.AreNotEqual(
            firstSession,
            sessions.CurrentSession!.SessionId);
        Assert.AreEqual(0x2000UL, service.Entries.Single().Address);
    }

    [TestMethod]
    public void ResultGridEntryCanBeAddedToWatchWindow()
    {
        var sessions = new MutableSessionService();
        using var service = new WatchService(
            new DelegateMemoryReaderService(),
            sessions,
            TimeProvider.System);
        using var viewModel = new WatchWindowViewModel(
            service,
            new TestLogger());
        var row = new ResultGridRowViewModel(
            new ResultGridItem(
                0x7FFF1234,
                ScanValueType.UInt16,
                BitConverter.GetBytes((ushort)42),
                ResultReadStatus.Available));

        var result = viewModel.AddFromResult(row);
        MemoryEditRequestedEventArgs? edited = null;
        viewModel.SelectedEntry = viewModel.Entries.Single();
        viewModel.EditValueRequested +=
            (_, eventArgs) => edited = eventArgs;
        viewModel.EditValueCommand.Execute(null);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, viewModel.Entries.Count);
        Assert.AreEqual(0x7FFF1234UL, viewModel.Entries[0].Address);
        Assert.AreEqual(
            ScanValueType.UInt16,
            viewModel.Entries[0].ValueType);
        Assert.IsTrue(
            viewModel.StatusMessage.Contains(
                "Memory Editor",
                StringComparison.Ordinal));
        Assert.AreEqual(0x7FFF1234UL, edited!.Address);
        Assert.AreEqual(
            MemoryInspector.Core.Memory.Editing.MemoryWriteSource.WatchWindow,
            edited.Source);
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(30_000)]
    public async Task LongRunningWatchRefreshStaysMemoryBounded()
    {
        const int entryCount = 16;
        const int refreshCount = 10_000;
        var sessions = new MutableSessionService();
        var reader = new DelegateMemoryReaderService
        {
            Batch = (requests, _) => BatchSuccess(
                requests,
                request => BitConverter.GetBytes(
                    checked((int)request.Address))),
        };
        using var service = new WatchService(
            reader,
            sessions,
            TimeProvider.System);

        for (var index = 0; index < entryCount; index++)
        {
            var added = service.Add(
                (ulong)(0x1000 + index * sizeof(int)),
                ScanValueType.Int32);
            Assert.IsTrue(added.IsSuccess);
        }

        _ = await service.RefreshAsync();
        ForceCollection();
        var retainedBefore = GC.GetTotalMemory(
            forceFullCollection: false);
        var timer = Stopwatch.StartNew();

        for (var index = 0; index < refreshCount; index++)
        {
            var result = await service.RefreshAsync();
            Assert.IsTrue(result.IsSuccess);
        }

        timer.Stop();
        ForceCollection();
        var retainedAfter = GC.GetTotalMemory(
            forceFullCollection: false);
        var retainedGrowth = Math.Max(
            0,
            retainedAfter - retainedBefore);
        var refreshesPerSecond =
            refreshCount / timer.Elapsed.TotalSeconds;
        TestContext.WriteLine(
            $"METRIC watch_refreshes_per_second=" +
            $"{refreshesPerSecond:F0}");
        Console.WriteLine(
            $"METRIC watch_refreshes_per_second=" +
            $"{refreshesPerSecond:F0}");
        TestContext.WriteLine(
            $"METRIC watch_retained_heap_growth_bytes=" +
            $"{retainedGrowth}");
        Console.WriteLine(
            $"METRIC watch_retained_heap_growth_bytes=" +
            $"{retainedGrowth}");

        Assert.AreEqual(
            refreshCount + 1,
            reader.BatchCallCount);
        Assert.AreEqual(entryCount, service.Entries.Count);
        Assert.IsTrue(service.Entries.All(entry =>
            entry.Status == WatchReadStatus.Available));
        Assert.IsTrue(
            retainedGrowth <= 32L * 1024 * 1024,
            $"Retained heap grew by {retainedGrowth:N0} bytes.");
        Assert.IsTrue(
            refreshesPerSecond >= 500,
            $"Watch refresh rate was {refreshesPerSecond:F0}/s.");
    }

    private static MemoryBatchReadItem SuccessItem(
        MemoryReadRequest request,
        byte[] data)
    {
        return new MemoryBatchReadItem(
            request,
            Result<MemoryReadResult>.Success(
                new MemoryReadResult(request, data)));
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static Task<Result<MemoryBatchReadResult>> BatchSuccess(
        IReadOnlyList<MemoryReadRequest> requests,
        Func<MemoryReadRequest, byte[]> getData)
    {
        return Task.FromResult(
            Result<MemoryBatchReadResult>.Success(
                new MemoryBatchReadResult(
                    requests.Select(request =>
                        SuccessItem(
                            request,
                            getData(request))))));
    }

    private sealed class DelegateMemoryReaderService
        : IMemoryReaderService
    {
        public Func<
            IReadOnlyList<MemoryReadRequest>,
            CancellationToken,
            Task<Result<MemoryBatchReadResult>>>? Batch { get; init; }

        public int BatchCallCount { get; private set; }

        public IReadOnlyList<MemoryReadRequest> LastRequests
        {
            get;
            private set;
        } = [];

        public Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new AssertFailedException(
                "Watch must use batch reads.");
        }

        public Task<Result<T>> TryReadAsync<T>(
            ulong address,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            throw new AssertFailedException(
                "Watch must use batch reads.");
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            IEnumerable<MemoryReadRequest> requests,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastRequests = requests.ToArray();
            BatchCallCount++;
            return Batch?.Invoke(
                LastRequests,
                cancellationToken) ??
                Task.FromResult(
                    Result<MemoryBatchReadResult>.Failure(
                        new Error(
                            ErrorCode.Unexpected,
                            "No batch response configured.")));
        }
    }

    private sealed class MutableSessionService
        : IMonitoringSessionService
    {
        public MutableSessionService()
        {
            ConnectNewSession();
        }

        public MonitoringSession? CurrentSession { get; private set; }

        public event EventHandler<MonitoringSessionChangedEventArgs>?
            SessionChanged;

        public void ConnectNewSession()
        {
            CurrentSession = new MonitoringSession
            {
                SessionId = Guid.NewGuid(),
                Identity = new MonitoringSessionIdentity(
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
                    "WatchTarget"),
                State = MonitoringSessionState.Connected,
                CreatedAt = DateTimeOffset.UtcNow,
                ConnectedAt = DateTimeOffset.UtcNow,
            };
            SessionChanged?.Invoke(
                this,
                new MonitoringSessionChangedEventArgs(
                    CurrentSession));
        }

        public void Transition(
            MonitoringSessionState state,
            string message)
        {
            CurrentSession = CurrentSession! with
            {
                State = state,
                EndedAt = DateTimeOffset.UtcNow,
                StatusMessage = message,
            };
            SessionChanged?.Invoke(
                this,
                new MonitoringSessionChangedEventArgs(
                    CurrentSession));
        }

        public Task<Result<MonitoringSession>> StartAsync(
            MonitoringSessionIdentity identity,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<MonitoringSession>> CheckLivenessAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> StopAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

using System.Runtime.CompilerServices;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Scanning.Snapshots;

namespace MemoryInspector.IntegrationTests.Scanning;

[TestClass]
public sealed class DurationFilterServiceTests
{
    private const ulong BaseAddress = 0x2_000;

    [TestMethod]
    public async Task EndpointCompareUsesOnlyStartAndEndValues()
    {
        using var fixture = new DurationFixture();
        var previous = await fixture.WritePreviousAsync(10, 20);
        fixture.Reader.Samples =
        [
            [10, 25],
        ];
        var progress = new CapturingProgress();

        var result = await fixture.Service.FilterAsync(
            CreateRequest(
                previous,
                targetNodeId: 2,
                ScanComparisonMode.Unchanged,
                DurationFilterObservationMode.EndpointCompare),
            progress: progress);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1L, result.Value.SampleCount);
        Assert.AreEqual(1L, result.Value.MatchedCount);
        Assert.AreEqual(1L, result.Value.HasChangedCount);
        CollectionAssert.AreEqual(
            new[] { BaseAddress },
            await fixture.ReadAddressesAsync(
                result.Value.Snapshot));
        Assert.AreEqual(
            100d,
            progress.Reports[^1].Percentage);
    }

    [TestMethod]
    public async Task ContinuousChangedKeepsValueThatChangedThenReturned()
    {
        using var fixture = new DurationFixture();
        var previous = await fixture.WritePreviousAsync(10, 20);
        fixture.Reader.Samples =
        [
            [11, 20],
            [10, 20],
            [10, 20],
        ];

        var result = await fixture.Service.FilterAsync(
            CreateRequest(
                previous,
                targetNodeId: 2,
                ScanComparisonMode.Changed,
                DurationFilterObservationMode.ContinuousObserve));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3L, result.Value.SampleCount);
        Assert.AreEqual(1L, result.Value.MatchedCount);
        Assert.AreEqual(1L, result.Value.HasChangedCount);
        Assert.AreEqual(1L, result.Value.HasIncreasedCount);
        Assert.AreEqual(1L, result.Value.HasDecreasedCount);
        CollectionAssert.AreEqual(
            new[] { BaseAddress },
            await fixture.ReadAddressesAsync(
                result.Value.Snapshot));
    }

    [TestMethod]
    public async Task ContinuousUnchangedKeepsOnlyNeverChangedValues()
    {
        using var fixture = new DurationFixture();
        var previous = await fixture.WritePreviousAsync(10, 20);
        fixture.Reader.Samples =
        [
            [11, 20],
            [10, 20],
            [10, 20],
        ];

        var result = await fixture.Service.FilterAsync(
            CreateRequest(
                previous,
                targetNodeId: 2,
                ScanComparisonMode.Unchanged,
                DurationFilterObservationMode.ContinuousObserve));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1L, result.Value.MatchedCount);
        CollectionAssert.AreEqual(
            new[] { BaseAddress + 1 },
            await fixture.ReadAddressesAsync(
                result.Value.Snapshot));
    }

    [TestMethod]
    [DataRow(ScanComparisonMode.Increased, 0)]
    [DataRow(ScanComparisonMode.Decreased, 1)]
    public async Task ContinuousObserveAccumulatesDirectionFlags(
        ScanComparisonMode mode,
        int expectedIndex)
    {
        using var fixture = new DurationFixture();
        var previous = await fixture.WritePreviousAsync(10, 10);
        fixture.Reader.Samples =
        [
            [11, 9],
            [11, 9],
            [11, 9],
        ];

        var result = await fixture.Service.FilterAsync(
            CreateRequest(
                previous,
                targetNodeId: 2,
                mode,
                DurationFilterObservationMode.ContinuousObserve));

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { BaseAddress + (ulong)expectedIndex },
            await fixture.ReadAddressesAsync(
                result.Value.Snapshot));
    }

    [TestMethod]
    public async Task AnyFailedObservationFlagsAndExcludesCandidate()
    {
        using var fixture = new DurationFixture();
        var previous = await fixture.WritePreviousAsync(10);
        fixture.Reader.Batch = (requests, call, _) =>
        {
            if (call == 2)
            {
                return Task.FromResult(
                    Result<MemoryBatchReadResult>.Success(
                        new MemoryBatchReadResult(
                        requests.Select(request =>
                            new MemoryBatchReadItem(
                                request,
                                Result<MemoryReadResult>.Failure(
                                    new Error(
                                        ErrorCode.NotFound,
                                        "Address became unreadable.")))))));
            }

            return CompleteBatch(requests, [10]);
        };

        var result = await fixture.Service.FilterAsync(
            CreateRequest(
                previous,
                targetNodeId: 2,
                ScanComparisonMode.Unchanged,
                DurationFilterObservationMode.ContinuousObserve));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0L, result.Value.MatchedCount);
        Assert.AreEqual(1L, result.Value.FailedObservationCount);
        Assert.AreEqual(1L, result.Value.ReadFailedCandidateCount);
        Assert.AreEqual(1, result.Value.Warnings.Count);
        Assert.IsTrue(result.Value.IsPartial);
    }

    [TestMethod]
    public async Task PauseStopsCountdownUntilResumed()
    {
        using var fixture = new DurationFixture();
        var previous = await fixture.WritePreviousAsync(10);
        fixture.Reader.Samples =
        [
            [10],
        ];
        var control = new DurationFilterExecutionControl();
        control.Pause();
        var operation = fixture.Service.FilterAsync(
            new DurationFilterRequest(
                previous,
                targetNodeId: 2,
                CreateFilter(ScanComparisonMode.Unchanged),
                duration: TimeSpan.FromMilliseconds(40),
                DurationFilterObservationMode.EndpointCompare,
                sampleInterval: TimeSpan.FromMilliseconds(20)),
            control);

        await Task.Delay(80);

        Assert.IsFalse(operation.IsCompleted);
        Assert.AreEqual(0, fixture.Reader.BatchCallCount);

        control.Resume();
        var result = await operation;

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, fixture.Reader.BatchCallCount);
    }

    [TestMethod]
    public async Task CancellationDuringCountdownDoesNotCommitSnapshot()
    {
        using var fixture = new DurationFixture();
        using var cancellation = new CancellationTokenSource();
        var previous = await fixture.WritePreviousAsync(10);
        var operation = fixture.Service.FilterAsync(
            new DurationFilterRequest(
                previous,
                targetNodeId: 2,
                CreateFilter(ScanComparisonMode.Unchanged),
                duration: TimeSpan.FromSeconds(2),
                DurationFilterObservationMode.EndpointCompare),
            cancellationToken: cancellation.Token);

        cancellation.CancelAfter(TimeSpan.FromMilliseconds(30));
        var result = await operation;

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Cancelled, result.Error.Code);
        Assert.IsFalse(File.Exists(fixture.SnapshotPath(2)));
    }

    [TestMethod]
    public async Task SessionChangeDuringCountdownDoesNotCommitSnapshot()
    {
        using var fixture = new DurationFixture();
        var previous = await fixture.WritePreviousAsync(10);
        var operation = fixture.Service.FilterAsync(
            new DurationFilterRequest(
                previous,
                targetNodeId: 2,
                CreateFilter(ScanComparisonMode.Unchanged),
                duration: TimeSpan.FromMilliseconds(250),
                DurationFilterObservationMode.EndpointCompare));

        await Task.Delay(30);
        fixture.SessionService.CurrentSession =
            fixture.SessionService.CurrentSession! with
            {
                State = MonitoringSessionState.Disconnected,
            };
        var result = await operation;

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.IsFalse(File.Exists(fixture.SnapshotPath(2)));
    }

    [TestMethod]
    public async Task RequestRejectsUnsupportedModeAndInvalidDuration()
    {
        using var fixture = new DurationFixture();
        var previous = await fixture.WritePreviousAsync(10);

        Assert.ThrowsExactly<ArgumentException>(
            () => new DurationFilterRequest(
                previous,
                targetNodeId: 2,
                CreateFilter(ScanComparisonMode.ExactValue),
                TimeSpan.FromSeconds(1),
                DurationFilterObservationMode.EndpointCompare));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new DurationFilterRequest(
                previous,
                targetNodeId: 2,
                CreateFilter(ScanComparisonMode.Unchanged),
                TimeSpan.Zero,
                DurationFilterObservationMode.EndpointCompare));
    }

    private static DurationFilterRequest CreateRequest(
        SnapshotDescriptor previous,
        int targetNodeId,
        ScanComparisonMode mode,
        DurationFilterObservationMode observationMode)
    {
        return new DurationFilterRequest(
            previous,
            targetNodeId,
            CreateFilter(mode),
            duration: TimeSpan.FromMilliseconds(60),
            observationMode,
            sampleInterval: TimeSpan.FromMilliseconds(20),
            pageSize: 10);
    }

    private static ScanRequest CreateFilter(
        ScanComparisonMode mode)
    {
        ScanValue? searchValue = mode ==
            ScanComparisonMode.ExactValue
            ? ScanValue.FromBytes(
                ScanValueType.Int32,
                BitConverter.GetBytes(10)).Value
            : null;
        return ScanRequest.Create(
            ScanValueType.Int32,
            mode,
            searchValue,
            ScanAlignmentMode.Aligned).Value;
    }

    private static Task<Result<MemoryBatchReadResult>>
        CompleteBatch(
            IReadOnlyList<MemoryReadRequest> requests,
            IReadOnlyList<int> values)
    {
        return Task.FromResult(
            Result<MemoryBatchReadResult>.Success(
                new MemoryBatchReadResult(
                    requests.Select((request, index) =>
                        new MemoryBatchReadItem(
                            request,
                            Result<MemoryReadResult>.Success(
                                new MemoryReadResult(
                                    request,
                                    BitConverter.GetBytes(
                                        values[index]))))))));
    }

    private sealed class DurationFixture : IDisposable
    {
        private readonly string _root;

        public DurationFixture()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "MemoryInspector.Tests",
                Guid.NewGuid().ToString("N"));
            var paths = new AppPathService(
                Path.Combine(_root, "ApplicationData"));
            SessionService = new StubSessionService
            {
                CurrentSession = CreateSession(),
            };
            Reader = new SequencedMemoryReaderService();
            Storage = new BinarySnapshotStorage(
                paths,
                TimeProvider.System);
            Service = new DurationFilterService(
                SessionService,
                Reader,
                Storage,
                new DefaultValueMatcher(),
                TimeProvider.System);
        }

        public StubSessionService SessionService { get; }

        public SequencedMemoryReaderService Reader { get; }

        public BinarySnapshotStorage Storage { get; }

        public IDurationFilterService Service { get; }

        public async Task<SnapshotDescriptor> WritePreviousAsync(
            params int[] values)
        {
            var result = await Storage.WriteAsync(
                new SnapshotWriteRequest(
                    SessionService.CurrentSession!.SessionId,
                    nodeId: 1,
                    ScanValueType.Int32,
                    includeValues: true,
                    expectedRecordCount: values.Length),
                Records(values));
            return result.Value;
        }

        public async Task<ulong[]> ReadAddressesAsync(
            SnapshotDescriptor snapshot)
        {
            var page = await Storage.ReadPageAsync(
                snapshot,
                pageNumber: 1,
                pageSize: 100);
            return page.Value.Items
                .Select(record => record.Candidate.Address)
                .ToArray();
        }

        public string SnapshotPath(int nodeId)
        {
            return Path.Combine(
                _root,
                "ApplicationData",
                "Temp",
                SessionService.CurrentSession!.SessionId
                    .ToString("D"),
                $"node_{nodeId:D4}.full.bin");
        }

        public void Dispose()
        {
            Storage.Dispose();

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static async IAsyncEnumerable<SnapshotRecord>
            Records(
                IReadOnlyList<int> values,
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < values.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new SnapshotRecord(
                    new CandidateAddress(
                        BaseAddress + (ulong)index),
                    BitConverter.GetBytes(values[index]));
            }

            await Task.CompletedTask;
        }

        private static MonitoringSession CreateSession()
        {
            return new MonitoringSession
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
                    "Target"),
                State = MonitoringSessionState.Connected,
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }
    }

    private sealed class SequencedMemoryReaderService
        : IMemoryReaderService
    {
        public IReadOnlyList<IReadOnlyList<int>> Samples { get; set; } =
            [];

        public Func<
            IReadOnlyList<MemoryReadRequest>,
            int,
            CancellationToken,
            Task<Result<MemoryBatchReadResult>>>? Batch { get; set; }

        public int BatchCallCount { get; private set; }

        public Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new AssertFailedException(
                "Duration Filter must use batch reads.");
        }

        public Task<Result<T>> TryReadAsync<T>(
            ulong address,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            throw new AssertFailedException(
                "Duration Filter must use batch reads.");
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            IEnumerable<MemoryReadRequest> requests,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var requestArray = requests.ToArray();
            var call = ++BatchCallCount;

            if (Batch is not null)
            {
                return Batch(
                    requestArray,
                    call,
                    cancellationToken);
            }

            return CompleteBatch(
                requestArray,
                Samples[call - 1]);
        }
    }

    private sealed class StubSessionService
        : IMonitoringSessionService
    {
        public MonitoringSession? CurrentSession { get; set; }

        public event EventHandler<MonitoringSessionChangedEventArgs>?
            SessionChanged
        {
            add { }
            remove { }
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

    private sealed class CapturingProgress
        : IProgress<OperationProgress>
    {
        public List<OperationProgress> Reports { get; } = [];

        public void Report(OperationProgress value)
        {
            Reports.Add(value);
        }
    }
}

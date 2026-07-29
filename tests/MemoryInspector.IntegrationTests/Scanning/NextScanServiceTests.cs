using System.Buffers.Binary;
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
public sealed class NextScanServiceTests
{
    private const ulong BaseAddress = 0x1_000;
    private readonly IScanValueParser _parser =
        new InvariantScanValueParser();

    [TestMethod]
    [DataRow(ScanComparisonMode.ExactValue, "20", "2")]
    [DataRow(ScanComparisonMode.Changed, null, "1,2")]
    [DataRow(ScanComparisonMode.Unchanged, null, "0")]
    [DataRow(ScanComparisonMode.Increased, null, "1")]
    [DataRow(ScanComparisonMode.Decreased, null, "2")]
    [DataRow(ScanComparisonMode.GreaterThan, "20", "1")]
    [DataRow(ScanComparisonMode.LessThan, "20", "0")]
    public async Task SupportsEveryNextScanComparisonMode(
        ScanComparisonMode mode,
        string? searchInput,
        string expectedIndexes)
    {
        using var fixture = new ScanFixture();
        var previous = await fixture.WritePreviousAsync(
            ScanValueType.Int32,
            Int32Values(10, 20, 30));
        var currentValues = Int32Values(10, 25, 20);
        fixture.Reader.Batch = CompleteBatch(currentValues);
        var request = new NextScanRequest(
            previous,
            targetNodeId: 2,
            CreateFilter(
                ScanValueType.Int32,
                mode,
                searchInput),
            pageSize: 2);

        var result = await fixture.Service.ScanAsync(request);
        var page = await fixture.Storage.ReadPageAsync(
            result.Value.Snapshot,
            pageNumber: 1,
            pageSize: 10);
        var expected = expectedIndexes.Length == 0
            ? Array.Empty<ulong>()
            : expectedIndexes
                .Split(',')
                .Select(index =>
                    BaseAddress + ulong.Parse(index))
                .ToArray();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3L, result.Value.ExaminedCount);
        Assert.AreEqual(
            expected.LongLength,
            result.Value.MatchedCount);
        CollectionAssert.AreEqual(
            expected,
            page.Value.Items
                .Select(record => record.Candidate.Address)
                .ToArray());

        foreach (var record in page.Value.Items)
        {
            var index = checked(
                (int)(record.Candidate.Address - BaseAddress));
            CollectionAssert.AreEqual(
                currentValues[index],
                record.Value.ToArray());
        }
    }

    [TestMethod]
    public async Task FloatingPointModesUseConfiguredTolerance()
    {
        using var fixture = new ScanFixture();
        var previous = await fixture.WritePreviousAsync(
            ScanValueType.Double,
            DoubleValues(1, 2, 3));
        fixture.Reader.Batch = CompleteBatch(
            DoubleValues(1.0005, 2.01, 2.99));
        var unchangedRequest = new NextScanRequest(
            previous,
            targetNodeId: 2,
            CreateFilter(
                ScanValueType.Double,
                ScanComparisonMode.Unchanged,
                tolerance: 0.001));
        var changedRequest = new NextScanRequest(
            previous,
            targetNodeId: 3,
            CreateFilter(
                ScanValueType.Double,
                ScanComparisonMode.Changed,
                tolerance: 0.001));

        var unchanged = await fixture.Service.ScanAsync(
            unchangedRequest);
        var changed = await fixture.Service.ScanAsync(
            changedRequest);

        Assert.AreEqual(1L, unchanged.Value.MatchedCount);
        Assert.AreEqual(2L, changed.Value.MatchedCount);
    }

    [TestMethod]
    public async Task SignedAndUnsignedComparisonsKeepTheirNumericMeaning()
    {
        using var fixture = new ScanFixture();
        var signedPrevious = await fixture.WritePreviousAsync(
            ScanValueType.Int32,
            Int32Values(-1),
            nodeId: 1);
        fixture.Reader.Batch = CompleteBatch(Int32Values(-2));
        var signedResult = await fixture.Service.ScanAsync(
            new NextScanRequest(
                signedPrevious,
                targetNodeId: 2,
                CreateFilter(
                    ScanValueType.Int32,
                    ScanComparisonMode.Decreased)));
        var unsignedPrevious = await fixture.WritePreviousAsync(
            ScanValueType.UInt32,
            UInt32Values(0),
            nodeId: 3);
        fixture.Reader.Batch = CompleteBatch(
            UInt32Values(uint.MaxValue));
        var unsignedResult = await fixture.Service.ScanAsync(
            new NextScanRequest(
                unsignedPrevious,
                targetNodeId: 4,
                CreateFilter(
                    ScanValueType.UInt32,
                    ScanComparisonMode.Increased)));

        Assert.AreEqual(1L, signedResult.Value.MatchedCount);
        Assert.AreEqual(1L, unsignedResult.Value.MatchedCount);
    }

    [TestMethod]
    public async Task InvalidAndPartialAddressesAreReportedAndExcluded()
    {
        using var fixture = new ScanFixture();
        var previous = await fixture.WritePreviousAsync(
            ScanValueType.Int32,
            Int32Values(10, 20, 30));
        fixture.Reader.Batch = (requests, _) =>
        {
            var complete = new MemoryReadResult(
                requests[0],
                BitConverter.GetBytes(10));
            var partial = new MemoryReadResult(
                requests[2],
                new byte[] { 30, 0 },
                [
                    new Error(
                        ErrorCode.NotFound,
                        "Value became partially unreadable."),
                ]);
            return Task.FromResult(
                Result<MemoryBatchReadResult>.Success(
                    new MemoryBatchReadResult(
                    [
                        new MemoryBatchReadItem(
                            requests[0],
                            Result<MemoryReadResult>.Success(
                                complete)),
                        new MemoryBatchReadItem(
                            requests[1],
                            Result<MemoryReadResult>.Failure(
                                new Error(
                                    ErrorCode.NotFound,
                                    "Address is no longer valid."))),
                        new MemoryBatchReadItem(
                            requests[2],
                            Result<MemoryReadResult>.Success(
                                partial)),
                    ])));
        };
        var request = new NextScanRequest(
            previous,
            targetNodeId: 2,
            CreateFilter(
                ScanValueType.Int32,
                ScanComparisonMode.Unchanged));

        var result = await fixture.Service.ScanAsync(request);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3L, result.Value.ExaminedCount);
        Assert.AreEqual(1L, result.Value.CompleteReadCount);
        Assert.AreEqual(1L, result.Value.FailedReadCount);
        Assert.AreEqual(1L, result.Value.PartialReadCount);
        Assert.AreEqual(1L, result.Value.MatchedCount);
        Assert.AreEqual(2, result.Value.Warnings.Count);
        Assert.IsTrue(result.Value.IsPartial);
    }

    [TestMethod]
    public async Task ReadsOnlyPreviousCandidatesInPagedBatches()
    {
        using var fixture = new ScanFixture();
        var previousValues = Int32Values(1, 2, 3, 4, 5);
        var previous = await fixture.WritePreviousAsync(
            ScanValueType.Int32,
            previousValues);
        fixture.Reader.Batch = CompleteBatch(previousValues);
        var progress = new CapturingProgress();

        var result = await fixture.Service.ScanAsync(
            new NextScanRequest(
                previous,
                targetNodeId: 2,
                CreateFilter(
                    ScanValueType.Int32,
                    ScanComparisonMode.Unchanged),
                pageSize: 2),
            progress);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3, fixture.Reader.BatchCallCount);
        CollectionAssert.AreEqual(
            new[]
            {
                BaseAddress,
                BaseAddress + 1,
                BaseAddress + 2,
                BaseAddress + 3,
                BaseAddress + 4,
            },
            fixture.Reader.RequestedAddresses.ToArray());
        Assert.AreEqual(5L, progress.Reports[^1].Completed);
    }

    [TestMethod]
    public async Task WarningDetailsAreBoundedForManyInvalidAddresses()
    {
        using var fixture = new ScanFixture();
        var previous = await fixture.WritePreviousAsync(
            ScanValueType.Byte,
            Enumerable.Repeat(new byte[] { 1 }, 150).ToArray());
        fixture.Reader.Batch = (requests, _) =>
            Task.FromResult(
                Result<MemoryBatchReadResult>.Success(
                    new MemoryBatchReadResult(
                        requests.Select(request =>
                            new MemoryBatchReadItem(
                                request,
                                Result<MemoryReadResult>.Failure(
                                    new Error(
                                        ErrorCode.NotFound,
                                        "Invalid address.")))))));

        var result = await fixture.Service.ScanAsync(
            new NextScanRequest(
                previous,
                targetNodeId: 2,
                CreateFilter(
                    ScanValueType.Byte,
                    ScanComparisonMode.Changed),
                pageSize: 150));

        Assert.AreEqual(150L, result.Value.FailedReadCount);
        Assert.AreEqual(100, result.Value.Warnings.Count);
        Assert.AreEqual(
            50L,
            result.Value.SuppressedWarningCount);
        Assert.AreEqual(0L, result.Value.MatchedCount);
    }

    [TestMethod]
    public async Task SessionChangeAbortsWithoutCommittingNextSnapshot()
    {
        using var fixture = new ScanFixture();
        var values = Int32Values(10);
        var previous = await fixture.WritePreviousAsync(
            ScanValueType.Int32,
            values);
        fixture.Reader.Batch = async (requests, token) =>
        {
            var result = await CompleteBatch(values)(
                requests,
                token);
            fixture.SessionService.CurrentSession =
                fixture.SessionService.CurrentSession! with
                {
                    State = MonitoringSessionState.Disconnected,
                };
            return result;
        };

        var result = await fixture.Service.ScanAsync(
            new NextScanRequest(
                previous,
                targetNodeId: 2,
                CreateFilter(
                    ScanValueType.Int32,
                    ScanComparisonMode.Unchanged)));

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.IsFalse(File.Exists(fixture.SnapshotPath(2)));
    }

    [TestMethod]
    public async Task CancelledBatchDoesNotCommitNextSnapshot()
    {
        using var fixture = new ScanFixture();
        using var cancellation = new CancellationTokenSource();
        var previous = await fixture.WritePreviousAsync(
            ScanValueType.Int32,
            Int32Values(10));
        fixture.Reader.Batch = (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<
                Result<MemoryBatchReadResult>>(token);
        };

        var result = await fixture.Service.ScanAsync(
            new NextScanRequest(
                previous,
                targetNodeId: 2,
                CreateFilter(
                    ScanValueType.Int32,
                    ScanComparisonMode.Unchanged)),
            cancellationToken: cancellation.Token);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Cancelled, result.Error.Code);
        Assert.IsFalse(File.Exists(fixture.SnapshotPath(2)));
    }

    private ScanRequest CreateFilter(
        ScanValueType valueType,
        ScanComparisonMode mode,
        string? searchInput = null,
        double? tolerance = null)
    {
        var searchValue = searchInput is null
            ? null
            : _parser.Parse(searchInput, valueType).Value;
        return ScanRequest.Create(
            valueType,
            mode,
            searchValue,
            ScanAlignmentMode.Aligned,
            tolerance).Value;
    }

    private static Func<
        IReadOnlyList<MemoryReadRequest>,
        CancellationToken,
        Task<Result<MemoryBatchReadResult>>> CompleteBatch(
        IReadOnlyList<byte[]> values)
    {
        return (requests, _) =>
        {
            var items = requests
                .Select(request =>
                    new MemoryBatchReadItem(
                        request,
                        Result<MemoryReadResult>.Success(
                            new MemoryReadResult(
                                request,
                                values[checked(
                                    (int)(
                                        request.Address -
                                        BaseAddress))]))))
                .ToArray();
            return Task.FromResult(
                Result<MemoryBatchReadResult>.Success(
                    new MemoryBatchReadResult(items)));
        };
    }

    private static byte[][] Int32Values(params int[] values)
    {
        return values
            .Select(BitConverter.GetBytes)
            .ToArray();
    }

    private static byte[][] UInt32Values(params uint[] values)
    {
        return values
            .Select(value =>
            {
                var bytes = new byte[sizeof(uint)];
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes,
                    value);
                return bytes;
            })
            .ToArray();
    }

    private static byte[][] DoubleValues(params double[] values)
    {
        return values
            .Select(BitConverter.GetBytes)
            .ToArray();
    }

    private sealed class ScanFixture : IDisposable
    {
        private readonly string _root;

        public ScanFixture()
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
            Reader = new DelegateMemoryReaderService();
            Storage = new BinarySnapshotStorage(
                paths,
                TimeProvider.System);
            Service = new NextScanService(
                SessionService,
                Reader,
                Storage,
                new DefaultValueMatcher(),
                TimeProvider.System);
        }

        public StubSessionService SessionService { get; }

        public DelegateMemoryReaderService Reader { get; }

        public BinarySnapshotStorage Storage { get; }

        public INextScanService Service { get; }

        public async Task<SnapshotDescriptor> WritePreviousAsync(
            ScanValueType valueType,
            IReadOnlyList<byte[]> values,
            int nodeId = 1)
        {
            var result = await Storage.WriteAsync(
                new SnapshotWriteRequest(
                    SessionService.CurrentSession!.SessionId,
                    nodeId,
                    valueType,
                    includeValues: true,
                    expectedRecordCount: values.Count),
                Records(values));
            return result.Value;
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
                IReadOnlyList<byte[]> values,
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < values.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new SnapshotRecord(
                    new CandidateAddress(
                        BaseAddress + (ulong)index),
                    values[index]);
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

    private sealed class DelegateMemoryReaderService
        : IMemoryReaderService
    {
        public Func<
            IReadOnlyList<MemoryReadRequest>,
            CancellationToken,
            Task<Result<MemoryBatchReadResult>>>? Batch { get; set; }

        public int BatchCallCount { get; private set; }

        public List<ulong> RequestedAddresses { get; } = [];

        public Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new AssertFailedException(
                "Next Scan must use batch reads.");
        }

        public Task<Result<T>> TryReadAsync<T>(
            ulong address,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            throw new AssertFailedException(
                "Next Scan must use batch reads.");
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            IEnumerable<MemoryReadRequest> requests,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var requestArray = requests.ToArray();
            BatchCallCount++;
            RequestedAddresses.AddRange(
                requestArray.Select(request => request.Address));
            return Batch?.Invoke(
                requestArray,
                cancellationToken) ??
                Task.FromResult(
                    Result<MemoryBatchReadResult>.Failure(
                        new Error(
                            ErrorCode.Unexpected,
                            "No batch response configured.")));
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

using System.Buffers.Binary;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Scanning.Snapshots;

namespace MemoryInspector.IntegrationTests.Scanning;

[TestClass]
public sealed class UnknownInitialScanServiceTests
{
    private const ulong BaseAddress = 0x1_000;

    [TestMethod]
    public async Task EstimateReportsAlignedCandidatesAndDiskUsage()
    {
        var readable = ReadableRegion(
            BaseAddress + 1,
            16);
        var unreadable = new MemoryRegion(
            BaseAddress + 0x100,
            16,
            BaseAddress + 0x100,
            MemoryRegionState.Committed,
            MemoryRegionType.Private,
            MemoryProtection.NoAccess);
        var settings = AppSettings.CreateDefault() with
        {
            MemoryOnlyThreshold = 3,
            SnapshotThreshold = 3,
        };
        using var fixture = new ScanFixture(
            [unreadable, readable],
            new DelegateMemoryReaderService(),
            settings);

        var result = await fixture.Service.EstimateAsync(
            ScanValueType.Int32,
            ScanAlignmentMode.Aligned);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3L, result.Value.CandidateCount);
        Assert.AreEqual(16L, result.Value.ScannableBytes);
        Assert.AreEqual(12, result.Value.RecordSize);
        Assert.AreEqual(164L, result.Value.EstimatedDiskBytes);
        Assert.AreEqual(1, result.Value.ScannableRegionCount);
        Assert.AreEqual(1, result.Value.SkippedRegionCount);
        Assert.IsTrue(result.Value.RequiresDiskBackedStorage);
    }

    [TestMethod]
    public async Task CreatesDiskBackedSnapshotWithInitialInt32Values()
    {
        var memory = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(memory, 10);
        BinaryPrimitives.WriteInt32LittleEndian(
            memory.AsSpan(4),
            20);
        BinaryPrimitives.WriteInt32LittleEndian(
            memory.AsSpan(8),
            30);
        using var fixture = new ScanFixture(
            [ReadableRegion(BaseAddress, (ulong)memory.Length)],
            BufferReader(BaseAddress, memory));

        var result = await fixture.Service.CreateSnapshotAsync(
            new UnknownInitialScanRequest(
                nodeId: 1,
                ScanValueType.Int32,
                ScanAlignmentMode.Aligned,
                chunkSizeBytes: 6));
        var page = await fixture.Storage.ReadPageAsync(
            result.Value.Snapshot,
            pageNumber: 1,
            pageSize: 10);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value.IsDiskBacked);
        Assert.IsFalse(result.Value.IsPartial);
        Assert.AreEqual(3L, result.Value.CandidateCount);
        Assert.AreEqual(
            ScanValueType.Int32,
            result.Value.Snapshot.ValueType);
        Assert.IsTrue(result.Value.Snapshot.IncludesValues);
        Assert.AreEqual(3, page.Value.Items.Count);
        CollectionAssert.AreEqual(
            new[] { 10, 20, 30 },
            page.Value.Items
                .Select(record =>
                    BinaryPrimitives.ReadInt32LittleEndian(
                        record.Value.Span))
                .ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                BaseAddress,
                BaseAddress + 4,
                BaseAddress + 8,
            },
            page.Value.Items
                .Select(record => record.Candidate.Address)
                .ToArray());
    }

    [TestMethod]
    public async Task UnalignedScanCapturesEveryValidStartAddress()
    {
        var memory = new byte[] { 1, 2, 3, 4, 5, 6 };
        using var fixture = new ScanFixture(
            [ReadableRegion(BaseAddress, (ulong)memory.Length)],
            BufferReader(BaseAddress, memory));

        var result = await fixture.Service.CreateSnapshotAsync(
            new UnknownInitialScanRequest(
                nodeId: 2,
                ScanValueType.UInt16,
                ScanAlignmentMode.Unaligned,
                chunkSizeBytes: 4));
        var page = await fixture.Storage.ReadPageAsync(
            result.Value.Snapshot,
            pageNumber: 1,
            pageSize: 10);

        Assert.AreEqual(5L, result.Value.Estimate.CandidateCount);
        Assert.AreEqual(5L, result.Value.CandidateCount);
        CollectionAssert.AreEqual(
            new ulong[]
            {
                BaseAddress,
                BaseAddress + 1,
                BaseAddress + 2,
                BaseAddress + 3,
                BaseAddress + 4,
            },
            page.Value.Items
                .Select(record => record.Candidate.Address)
                .ToArray());
        CollectionAssert.AreEqual(
            new ushort[]
            {
                0x0201,
                0x0302,
                0x0403,
                0x0504,
                0x0605,
            },
            page.Value.Items
                .Select(record =>
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        record.Value.Span))
                .ToArray());
    }

    [TestMethod]
    public async Task PartialReadPersistsOnlyAvailableCompleteValues()
    {
        var warning = new Error(
            ErrorCode.NotFound,
            "The second half is unavailable.");
        var reader = new DelegateMemoryReaderService
        {
            Read = (address, length, _) =>
                Task.FromResult(
                    Result<MemoryReadResult>.Success(
                        new MemoryReadResult(
                            new MemoryReadRequest(
                                address,
                                length),
                            BitConverter.GetBytes(42),
                            [warning]))),
        };
        using var fixture = new ScanFixture(
            [ReadableRegion(BaseAddress, 8)],
            reader);

        var result = await fixture.Service.CreateSnapshotAsync(
            new UnknownInitialScanRequest(
                nodeId: 1,
                ScanValueType.Int32,
                ScanAlignmentMode.Aligned,
                chunkSizeBytes: 8));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2L, result.Value.Estimate.CandidateCount);
        Assert.AreEqual(1L, result.Value.CandidateCount);
        Assert.AreEqual(4L, result.Value.ScannedBytes);
        Assert.AreEqual(1, result.Value.Warnings.Count);
        Assert.IsTrue(result.Value.IsPartial);
    }

    [TestMethod]
    public async Task OverlappingRegionsDoNotDuplicateCandidates()
    {
        var memory = BitConverter.GetBytes(42);
        var region = ReadableRegion(
            BaseAddress,
            (ulong)memory.Length);
        using var fixture = new ScanFixture(
            [region, region],
            BufferReader(BaseAddress, memory));

        var estimate = await fixture.Service.EstimateAsync(
            ScanValueType.Int32,
            ScanAlignmentMode.Aligned);
        var result = await fixture.Service.CreateSnapshotAsync(
            new UnknownInitialScanRequest(
                nodeId: 1,
                ScanValueType.Int32,
                ScanAlignmentMode.Aligned,
                chunkSizeBytes: 4));

        Assert.AreEqual(1L, estimate.Value.CandidateCount);
        Assert.AreEqual(1L, result.Value.CandidateCount);
    }

    [TestMethod]
    public async Task CancellationDoesNotCommitSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new DelegateMemoryReaderService
        {
            Read = (_, _, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<Result<MemoryReadResult>>(
                    token);
            },
        };
        using var fixture = new ScanFixture(
            [ReadableRegion(BaseAddress, 8)],
            reader);

        var result = await fixture.Service.CreateSnapshotAsync(
            new UnknownInitialScanRequest(
                nodeId: 1,
                ScanValueType.Int32,
                ScanAlignmentMode.Aligned,
                chunkSizeBytes: 8),
            cancellationToken: cancellation.Token);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Cancelled, result.Error.Code);
        Assert.IsFalse(File.Exists(fixture.SnapshotPath(1)));
    }

    [TestMethod]
    public async Task SessionChangeAbortsBeforeSnapshotCommit()
    {
        var reader = BufferReader(
            BaseAddress,
            BitConverter.GetBytes(42));
        using var fixture = new ScanFixture(
            [ReadableRegion(BaseAddress, 4)],
            reader);
        reader.AfterRead = () =>
        {
            fixture.SessionService.CurrentSession =
                fixture.SessionService.CurrentSession! with
                {
                    State = MonitoringSessionState.Disconnected,
                };
        };

        var result = await fixture.Service.CreateSnapshotAsync(
            new UnknownInitialScanRequest(
                nodeId: 1,
                ScanValueType.Int32,
                ScanAlignmentMode.Aligned,
                chunkSizeBytes: 4));

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.IsFalse(File.Exists(fixture.SnapshotPath(1)));
    }

    [TestMethod]
    public async Task DisconnectedSessionRejectsEstimateBeforeRegionQuery()
    {
        using var fixture = new ScanFixture(
            [],
            new DelegateMemoryReaderService());
        fixture.SessionService.CurrentSession =
            fixture.SessionService.CurrentSession! with
            {
                State = MonitoringSessionState.Disconnected,
            };

        var result = await fixture.Service.EstimateAsync(
            ScanValueType.Byte,
            ScanAlignmentMode.Aligned);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.AreEqual(0, fixture.RegionService.CallCount);
    }

    [TestMethod]
    public async Task InvalidValueSelectionReturnsValidation()
    {
        using var fixture = new ScanFixture(
            [],
            new DelegateMemoryReaderService());

        var result = await fixture.Service.EstimateAsync(
            (ScanValueType)999,
            ScanAlignmentMode.Aligned);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
        Assert.AreEqual(0, fixture.RegionService.CallCount);
    }

    [TestMethod]
    public async Task LargeCaptureReturnsMetadataInsteadOfCandidateList()
    {
        const int byteCount = 100_000;
        var memory = new byte[byteCount];
        var progress = new CapturingProgress();
        using var fixture = new ScanFixture(
            [ReadableRegion(BaseAddress, byteCount)],
            BufferReader(BaseAddress, memory),
            AppSettings.CreateDefault() with
            {
                MemoryOnlyThreshold = 10_000,
                SnapshotThreshold = 10_000,
            });

        var result = await fixture.Service.CreateSnapshotAsync(
            new UnknownInitialScanRequest(
                nodeId: 3,
                ScanValueType.Byte,
                ScanAlignmentMode.Unaligned,
                chunkSizeBytes: 4096),
            progress);
        var lastPage = await fixture.Storage.ReadPageAsync(
            result.Value.Snapshot,
            pageNumber: 100,
            pageSize: 1000);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(byteCount, result.Value.CandidateCount);
        Assert.IsTrue(
            result.Value.Estimate.RequiresDiskBackedStorage);
        Assert.IsTrue(result.Value.IsDiskBacked);
        Assert.AreEqual(1000, lastPage.Value.Items.Count);
        Assert.AreEqual(
            BaseAddress + byteCount - 1,
            lastPage.Value.Items[^1].Candidate.Address);
        Assert.IsTrue(progress.Reports.Count < 100);
        Assert.AreEqual(
            byteCount,
            progress.Reports[^1].Completed);
    }

    private static DelegateMemoryReaderService BufferReader(
        ulong baseAddress,
        byte[] memory)
    {
        return new DelegateMemoryReaderService
        {
            Read = (address, length, _) =>
            {
                var offset = checked(
                    (int)(address - baseAddress));
                var available = Math.Min(
                    length,
                    memory.Length - offset);
                var data = memory
                    .AsSpan(offset, available)
                    .ToArray();
                return Task.FromResult(
                    Result<MemoryReadResult>.Success(
                        new MemoryReadResult(
                            new MemoryReadRequest(
                                address,
                                length),
                            data)));
            },
        };
    }

    private static MemoryRegion ReadableRegion(
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

    private sealed class ScanFixture : IDisposable
    {
        private readonly string _root;

        public ScanFixture(
            IReadOnlyList<MemoryRegion> regions,
            DelegateMemoryReaderService reader,
            AppSettings? settings = null)
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
            RegionService = new DelegateMemoryRegionService(
                new MemoryRegionQueryResult(regions));
            Storage = new BinarySnapshotStorage(
                paths,
                TimeProvider.System);
            Service = new UnknownInitialScanService(
                SessionService,
                RegionService,
                reader,
                Storage,
                new StubSettingsService(
                    settings ??
                    AppSettings.CreateDefault()),
                TimeProvider.System);
        }

        public StubSessionService SessionService { get; }

        public DelegateMemoryRegionService RegionService { get; }

        public BinarySnapshotStorage Storage { get; }

        public IUnknownInitialScanService Service { get; }

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

    private sealed class DelegateMemoryRegionService(
        MemoryRegionQueryResult result) : IMemoryRegionService
    {
        public int CallCount { get; private set; }

        public Task<Result<MemoryRegionQueryResult>> GetRegionsAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(
                Result<MemoryRegionQueryResult>.Success(result));
        }
    }

    private sealed class DelegateMemoryReaderService
        : IMemoryReaderService
    {
        public Func<
            ulong,
            int,
            CancellationToken,
            Task<Result<MemoryReadResult>>>? Read { get; init; }

        public Action? AfterRead { get; set; }

        public async Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var result = await (
                Read?.Invoke(
                    address,
                    length,
                    cancellationToken) ??
                Task.FromResult(
                    Result<MemoryReadResult>.Failure(
                        new Error(
                            ErrorCode.Unexpected,
                            "No read response configured."))));
            AfterRead?.Invoke();
            return result;
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

    private sealed class StubSettingsService(
        AppSettings settings) : ISettingsService
    {
        public Task<Result<AppSettings>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Result<AppSettings>.Success(settings));
        }

        public Task<Result> SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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

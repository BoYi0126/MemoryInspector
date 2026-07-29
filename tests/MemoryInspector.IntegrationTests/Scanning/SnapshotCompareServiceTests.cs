using System.Buffers.Binary;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Application.Scanning.Snapshots.Comparison;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.IntegrationTests.Scanning;

[TestClass]
public sealed class SnapshotCompareServiceTests
{
    private static readonly Guid SessionId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    [TestMethod]
    public async Task StreamingMergeClassifiesAndPagesUnion()
    {
        var left = Snapshot(1, 3, payloadLength: 36);
        var right = Snapshot(2, 3, payloadLength: 48);
        var storage = new GeneratedSnapshotStorage();
        storage.Add(
            left,
            [
                Record(0x1000, 1),
                Record(0x2000, 2),
                Record(0x4000, 4),
            ]);
        storage.Add(
            right,
            [
                Record(0x2000, 2),
                Record(0x3000, 3),
                Record(0x4000, 40),
            ]);
        var service = new SnapshotCompareService(storage);

        var result = await service.CompareAsync(
            left,
            right,
            pageNumber: 2,
            pageSize: 2);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Summary.AddedCount);
        Assert.AreEqual(1, result.Value.Summary.RemovedCount);
        Assert.AreEqual(1, result.Value.Summary.ChangedCount);
        Assert.AreEqual(1, result.Value.Summary.UnchangedCount);
        Assert.AreEqual(0, result.Value.Summary.CountDifference);
        Assert.AreEqual(
            12,
            result.Value.Summary.StorageSizeDifference);
        Assert.AreEqual(4, result.Value.Differences.TotalCount);
        CollectionAssert.AreEqual(
            new ulong[] { 0x3000, 0x4000 },
            result.Value.Differences.Items
                .Select(item => item.Address)
                .ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                SnapshotDifferenceKind.Added,
                SnapshotDifferenceKind.Changed,
            },
            result.Value.Differences.Items
                .Select(item => item.Kind)
                .ToArray());
    }

    [TestMethod]
    public async Task VisitStreamsEveryCategoryInAddressOrder()
    {
        var left = Snapshot(1, 2);
        var right = Snapshot(2, 2);
        var storage = new GeneratedSnapshotStorage();
        storage.Add(
            left,
            [Record(10, 1), Record(30, 3)]);
        storage.Add(
            right,
            [Record(20, 2), Record(30, 3)]);
        var visited = new List<SnapshotDifference>();
        var service = new SnapshotCompareService(storage);

        var result = await service.VisitAsync(
            left,
            right,
            (difference, _) =>
            {
                visited.Add(difference);
                return ValueTask.CompletedTask;
            });

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new ulong[] { 10, 20, 30 },
            visited.Select(item => item.Address).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                SnapshotDifferenceKind.Removed,
                SnapshotDifferenceKind.Added,
                SnapshotDifferenceKind.Unchanged,
            },
            visited.Select(item => item.Kind).ToArray());
    }

    [TestMethod]
    public async Task IncompatibleLayoutsFailBeforeStorageRead()
    {
        var left = Snapshot(1, 1);
        var right = new SnapshotDescriptor(
            SessionId,
            2,
            SnapshotFormatInfo.CurrentVersion,
            ScanValueType.Int64,
            includesValues: true,
            valueSize: sizeof(long),
            recordSize: sizeof(ulong) + sizeof(long),
            recordCount: 1,
            payloadLength: 16,
            checksum: "RIGHT",
            createdAt: DateTimeOffset.UtcNow,
            filePath: "right.snap");
        var storage = new GeneratedSnapshotStorage();
        var service = new SnapshotCompareService(storage);

        var result = await service.CompareAsync(left, right);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
        Assert.AreEqual(0, storage.ReadPageCallCount);
    }

    [TestMethod]
    public async Task UnorderedSnapshotIsReportedAsSerializationError()
    {
        var left = Snapshot(1, 2);
        var right = Snapshot(2, 0);
        var storage = new GeneratedSnapshotStorage();
        storage.Add(
            left,
            [Record(20, 2), Record(10, 1)]);
        storage.Add(right, []);
        var service = new SnapshotCompareService(storage);

        var result = await service.CompareAsync(left, right);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(
            ErrorCode.Serialization,
            result.Error.Code);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task MillionRecordsUseBoundedStoragePages()
    {
        const long count = 1_000_000;
        var left = Snapshot(1, count);
        var right = Snapshot(2, count);
        var storage = new GeneratedSnapshotStorage();
        storage.AddGenerated(left);
        storage.AddGenerated(right);
        var service = new SnapshotCompareService(storage);

        var result = await service.CompareAsync(
            left,
            right,
            pageNumber: 1,
            pageSize: 500);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(count,
            result.Value.Summary.UnchangedCount);
        Assert.AreEqual(500,
            result.Value.Differences.Items.Count);
        Assert.IsTrue(
            storage.MaximumRequestedPageSize <= 4_096);
        Assert.IsTrue(storage.ReadPageCallCount < 500);
    }

    [TestMethod]
    public async Task OutOfRangeDifferencePageReturnsValidation()
    {
        var left = Snapshot(1, 1);
        var right = Snapshot(2, 1);
        var storage = new GeneratedSnapshotStorage();
        storage.Add(left, [Record(1, 1)]);
        storage.Add(right, [Record(1, 1)]);
        var service = new SnapshotCompareService(storage);

        var result = await service.CompareAsync(
            left,
            right,
            pageNumber: 2,
            pageSize: 1);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
    }

    internal static SnapshotDescriptor Snapshot(
        int nodeId,
        long count,
        long? payloadLength = null)
    {
        return new SnapshotDescriptor(
            SessionId,
            nodeId,
            SnapshotFormatInfo.CurrentVersion,
            ScanValueType.Int32,
            includesValues: true,
            valueSize: sizeof(int),
            recordSize: sizeof(ulong) + sizeof(int),
            recordCount: count,
            payloadLength:
                payloadLength ?? checked(count * 12),
            checksum: $"NODE-{nodeId}",
            createdAt: DateTimeOffset.UtcNow,
            filePath: $"node-{nodeId}.snap");
    }

    internal static SnapshotRecord Record(
        ulong address,
        int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return new SnapshotRecord(
            new CandidateAddress(address),
            bytes);
    }

    internal sealed class GeneratedSnapshotStorage :
        ISnapshotStorage
    {
        private readonly Dictionary<
            int,
            IReadOnlyList<SnapshotRecord>> _records = [];
        private readonly HashSet<int> _generated = [];

        public int ReadPageCallCount { get; private set; }

        public int MaximumRequestedPageSize { get; private set; }

        public void Add(
            SnapshotDescriptor snapshot,
            IReadOnlyList<SnapshotRecord> records)
        {
            _records.Add(snapshot.NodeId, records);
        }

        public void AddGenerated(SnapshotDescriptor snapshot)
        {
            _generated.Add(snapshot.NodeId);
        }

        public Task<Result<PagedResult<SnapshotRecord>>> ReadPageAsync(
            SnapshotDescriptor snapshot,
            long pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            ReadPageCallCount++;
            MaximumRequestedPageSize = Math.Max(
                MaximumRequestedPageSize,
                pageSize);
            var start = checked((pageNumber - 1) * pageSize);
            var count = (int)Math.Min(
                pageSize,
                Math.Max(0, snapshot.RecordCount - start));
            SnapshotRecord[] page;

            if (_generated.Contains(snapshot.NodeId))
            {
                page = Enumerable.Range(0, count)
                    .Select(index =>
                    {
                        var ordinal = start + index;
                        return Record(
                            checked((ulong)ordinal + 1),
                            checked((int)ordinal));
                    })
                    .ToArray();
            }
            else
            {
                page = _records[snapshot.NodeId]
                    .Skip(checked((int)start))
                    .Take(count)
                    .ToArray();
            }

            return Task.FromResult(
                Result<PagedResult<SnapshotRecord>>.Success(
                    new PagedResult<SnapshotRecord>(
                        page,
                        pageNumber,
                        pageSize,
                        snapshot.RecordCount)));
        }

        public Task<Result<SnapshotDescriptor>> WriteAsync(
            SnapshotWriteRequest request,
            IAsyncEnumerable<SnapshotRecord> records,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<SnapshotDescriptor>> OpenAsync(
            Guid sessionId,
            int nodeId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<SnapshotDescriptor>> OptimizeAsync(
            SnapshotDescriptor parentSnapshot,
            SnapshotDescriptor fullSnapshot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> DeleteAsync(
            Guid sessionId,
            int nodeId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<SnapshotRecoveryResult>>
            RecoverIncompleteAsync(
                Guid sessionId,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}

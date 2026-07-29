using System.Buffers.Binary;
using MemoryInspector.Application.Scanning.Results;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.IntegrationTests.Scanning;

[TestClass]
public sealed class ResultGridServiceTests
{
    [TestMethod]
    public async Task MapsOneSnapshotPageWithoutMaterializingOtherPages()
    {
        var snapshot = CreateSnapshot(
            recordCount: 1_000_000,
            includesValues: true);
        var storage = new RecordingSnapshotStorage(
            (requested, pageNumber, pageSize, _) =>
            {
                var value = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32LittleEndian(
                    value,
                    42);
                SnapshotRecord[] records =
                [
                    new(
                        new CandidateAddress(0x1234),
                        value),
                ];
                return Task.FromResult(
                    Result<PagedResult<SnapshotRecord>>.Success(
                        new PagedResult<SnapshotRecord>(
                            records,
                            pageNumber,
                            pageSize,
                            requested.RecordCount)));
            });
        var service = new ResultGridService(storage);

        var result = await service.LoadPageAsync(
            snapshot,
            pageNumber: 500,
            pageSize: 1_000);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1_000_000L, result.Value.TotalCount);
        Assert.AreEqual(1, result.Value.Items.Count);
        Assert.AreEqual(
            ResultReadStatus.Available,
            result.Value.Items[0].ReadStatus);
        Assert.AreEqual(42, BinaryPrimitives.ReadInt32LittleEndian(
            result.Value.Items[0].Value.Span));
        Assert.AreEqual(1, storage.ReadPageCallCount);
    }

    [TestMethod]
    public async Task AddressOnlySnapshotReportsAddressOnlyReadStatus()
    {
        var snapshot = CreateSnapshot(
            recordCount: 1,
            includesValues: false);
        var storage = new RecordingSnapshotStorage(
            (requested, pageNumber, pageSize, _) =>
                Task.FromResult(
                    Result<PagedResult<SnapshotRecord>>.Success(
                        new PagedResult<SnapshotRecord>(
                            [
                                new SnapshotRecord(
                                    new CandidateAddress(0xABCD)),
                            ],
                            pageNumber,
                            pageSize,
                            requested.RecordCount))));
        var service = new ResultGridService(storage);

        var result = await service.LoadPageAsync(
            snapshot,
            pageNumber: 1,
            pageSize: 1_000);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            ResultReadStatus.AddressOnly,
            result.Value.Items[0].ReadStatus);
        Assert.AreEqual(0, result.Value.Items[0].Value.Length);
    }

    internal static SnapshotDescriptor CreateSnapshot(
        long recordCount,
        bool includesValues = true,
        int nodeId = 1)
    {
        var valueSize = includesValues ? sizeof(int) : 0;
        var recordSize = sizeof(ulong) + valueSize;
        return new SnapshotDescriptor(
            Guid.NewGuid(),
            nodeId,
            SnapshotFormatInfo.CurrentVersion,
            ScanValueType.Int32,
            includesValues,
            valueSize,
            recordSize,
            recordCount,
            checked(recordCount * recordSize),
            new string('A', 64),
            DateTimeOffset.UtcNow,
            Path.GetFullPath($"node_{nodeId:D4}.full.bin"));
    }

    private sealed class RecordingSnapshotStorage(
        Func<
            SnapshotDescriptor,
            long,
            int,
            CancellationToken,
            Task<Result<PagedResult<SnapshotRecord>>>> readPage)
        : ISnapshotStorage
    {
        private int _readPageCallCount;

        public int ReadPageCallCount =>
            Volatile.Read(ref _readPageCallCount);

        public Task<Result<PagedResult<SnapshotRecord>>> ReadPageAsync(
            SnapshotDescriptor snapshot,
            long pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readPageCallCount);
            return readPage(
                snapshot,
                pageNumber,
                pageSize,
                cancellationToken);
        }

        public Task<Result<SnapshotDescriptor>> WriteAsync(
            SnapshotWriteRequest request,
            IAsyncEnumerable<SnapshotRecord> records,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<SnapshotDescriptor>> OpenAsync(
            Guid sessionId,
            int nodeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<SnapshotDescriptor>> OptimizeAsync(
            SnapshotDescriptor parentSnapshot,
            SnapshotDescriptor fullSnapshot,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(
            Guid sessionId,
            int nodeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<SnapshotRecoveryResult>>
            RecoverIncompleteAsync(
                Guid sessionId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

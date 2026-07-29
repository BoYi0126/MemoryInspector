using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Scanning.Snapshots;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.Scanning.Snapshots;

[TestClass]
public sealed class BinarySnapshotDeltaStorageTests
{
    [TestMethod]
    public async Task DeltaRemoveRestoresRecordsAndProtectsSharedParent()
    {
        using var fixture = new DeltaFixture();
        var sessionId = Guid.NewGuid();
        var parent = await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            Enumerable.Range(0, 10));
        var firstFull = await fixture.WriteAsync(
            sessionId,
            nodeId: 2,
            new[] { 100 }.Concat(
                Enumerable.Range(1, 8)));
        var secondFull = await fixture.WriteAsync(
            sessionId,
            nodeId: 3,
            Enumerable.Range(0, 9));
        var first = await fixture.Storage.OptimizeAsync(
            parent,
            firstFull);
        var second = await fixture.Storage.OptimizeAsync(
            parent,
            secondFull);

        var page = await fixture.Storage.ReadPageAsync(
            first.Value,
            pageNumber: 1,
            pageSize: 5);
        var openedParent = await fixture.Storage.OpenAsync(
            sessionId,
            nodeId: 1);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(
            SnapshotStorageKind.DeltaRemove,
            first.Value.StorageKind);
        Assert.AreEqual(1, first.Value.ChainDepth);
        Assert.AreEqual(5, page.Value.Items.Count);
        Assert.AreEqual(
            100,
            ReadValue(page.Value.Items[0]));
        Assert.AreEqual(2, openedParent.Value.ReferenceCount);

        var deleteParent = await fixture.Storage.DeleteAsync(
            sessionId,
            nodeId: 1);
        Assert.IsTrue(deleteParent.IsFailure);

        Assert.IsTrue((await fixture.Storage.DeleteAsync(
            sessionId,
            nodeId: 2)).IsSuccess);
        openedParent = await fixture.Storage.OpenAsync(
            sessionId,
            nodeId: 1);
        Assert.AreEqual(1, openedParent.Value.ReferenceCount);
        Assert.IsTrue(File.Exists(parent.FilePath));

        Assert.IsTrue((await fixture.Storage.DeleteAsync(
            sessionId,
            nodeId: 3)).IsSuccess);
        Assert.IsTrue((await fixture.Storage.DeleteAsync(
            sessionId,
            nodeId: 1)).IsSuccess);
    }

    [TestMethod]
    public async Task DeltaKeepStoresSmallChangedSubset()
    {
        using var fixture = new DeltaFixture();
        var sessionId = Guid.NewGuid();
        var parent = await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            Enumerable.Range(0, 10));
        var full = await fixture.WriteAsync(
            sessionId,
            nodeId: 2,
            new[] { 100, 101 },
            addressIndexes: new[] { 0, 1 });

        var optimized = await fixture.Storage.OptimizeAsync(
            parent,
            full);
        var page = await fixture.Storage.ReadPageAsync(
            optimized.Value,
            pageNumber: 1,
            pageSize: 10);

        Assert.IsTrue(optimized.IsSuccess);
        Assert.AreEqual(
            SnapshotStorageKind.DeltaKeep,
            optimized.Value.StorageKind);
        CollectionAssert.AreEqual(
            new[] { 100, 101 },
            page.Value.Items
                .Select(ReadValue)
                .ToArray());
        Assert.IsFalse(File.Exists(full.FilePath));
        Assert.IsTrue(File.Exists(
            optimized.Value.FilePath));
    }

    [TestMethod]
    public async Task FifthDeltaInChainIsCompactedToFullSnapshot()
    {
        using var fixture = new DeltaFixture();
        var sessionId = Guid.NewGuid();
        var parent = await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            Enumerable.Range(0, 10));
        var depths = new List<int>();
        var kinds = new List<SnapshotStorageKind>();

        for (var nodeId = 2; nodeId <= 6; nodeId++)
        {
            var full = await fixture.WriteAsync(
                sessionId,
                nodeId,
                Enumerable.Range(0, 10));
            var optimized =
                await fixture.Storage.OptimizeAsync(
                    parent,
                    full);

            Assert.IsTrue(optimized.IsSuccess);
            parent = optimized.Value;
            depths.Add(parent.ChainDepth);
            kinds.Add(parent.StorageKind);
        }

        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4, 0 },
            depths);
        Assert.AreEqual(
            SnapshotStorageKind.Full,
            kinds[^1]);
        var page = await fixture.Storage.ReadPageAsync(
            parent,
            pageNumber: 1,
            pageSize: 10);
        Assert.AreEqual(10, page.Value.Items.Count);
    }

    [TestMethod]
    public async Task DeltaLargerThanHalfFullPayloadKeepsFullSnapshot()
    {
        using var fixture = new DeltaFixture();
        var sessionId = Guid.NewGuid();
        var parent = await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            Enumerable.Range(0, 100));
        var full = await fixture.WriteAsync(
            sessionId,
            nodeId: 2,
            Enumerable.Range(1_000, 60),
            Enumerable.Range(0, 60));

        var optimized = await fixture.Storage.OptimizeAsync(
            parent,
            full);

        Assert.IsTrue(optimized.IsSuccess);
        Assert.AreEqual(
            SnapshotStorageKind.Full,
            optimized.Value.StorageKind);
        Assert.AreEqual(0, optimized.Value.ChainDepth);
        Assert.IsTrue(File.Exists(full.FilePath));
    }

    [TestMethod]
    public async Task AccumulatedDeltaOverHalfParentPayloadCompactsToFull()
    {
        using var fixture = new DeltaFixture();
        var sessionId = Guid.NewGuid();
        var root = await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            Enumerable.Range(0, 100));
        var firstFull = await fixture.WriteAsync(
            sessionId,
            nodeId: 2,
            Enumerable.Range(0, 50));
        var firstDelta = await fixture.Storage.OptimizeAsync(
            root,
            firstFull);
        var secondFull = await fixture.WriteAsync(
            sessionId,
            nodeId: 3,
            Enumerable.Range(0, 50));

        var compacted = await fixture.Storage.OptimizeAsync(
            firstDelta.Value,
            secondFull);

        Assert.IsTrue(firstDelta.IsSuccess);
        Assert.AreEqual(
            SnapshotStorageKind.DeltaRemove,
            firstDelta.Value.StorageKind);
        Assert.AreEqual(
            400L,
            firstDelta.Value.AccumulatedDeltaBytes);
        Assert.IsTrue(compacted.IsSuccess);
        Assert.AreEqual(
            SnapshotStorageKind.Full,
            compacted.Value.StorageKind);
        Assert.AreEqual(
            0L,
            compacted.Value.AccumulatedDeltaBytes);
    }

    private static int ReadValue(SnapshotRecord record)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(
            record.Value.Span);
    }

    private sealed class DeltaFixture : IDisposable
    {
        private readonly TemporaryDirectory _temporaryDirectory =
            new();

        public DeltaFixture()
        {
            Storage = new BinarySnapshotStorage(
                new AppPathService(
                    _temporaryDirectory.RootPath),
                TimeProvider.System);
        }

        public BinarySnapshotStorage Storage { get; }

        public async Task<SnapshotDescriptor> WriteAsync(
            Guid sessionId,
            int nodeId,
            IEnumerable<int> values,
            IEnumerable<int>? addressIndexes = null)
        {
            var copiedValues = values.ToArray();
            var copiedIndexes = addressIndexes?.ToArray() ??
                Enumerable.Range(
                    0,
                    copiedValues.Length).ToArray();
            Assert.AreEqual(
                copiedValues.Length,
                copiedIndexes.Length);
            var result = await Storage.WriteAsync(
                new SnapshotWriteRequest(
                    sessionId,
                    nodeId,
                    ScanValueType.Int32,
                    includeValues: true,
                    expectedRecordCount:
                        copiedValues.Length),
                Records(copiedValues, copiedIndexes));

            Assert.IsTrue(
                result.IsSuccess,
                result.IsFailure
                    ? result.Error.ToDisplayMessage()
                    : null);
            return result.Value;
        }

        public void Dispose()
        {
            Storage.Dispose();
            _temporaryDirectory.Dispose();
        }
    }

    private static async IAsyncEnumerable<SnapshotRecord>
        Records(
            IReadOnlyList<int> values,
            IReadOnlyList<int> addressIndexes,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        for (var index = 0; index < values.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(
                value,
                values[index]);
            yield return new SnapshotRecord(
                new CandidateAddress(
                    checked(
                        (ulong)(
                            0x1_000 +
                            addressIndexes[index]))),
                value);
            await Task.Yield();
        }
    }
}

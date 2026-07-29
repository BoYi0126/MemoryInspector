using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Scanning.Snapshots;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.Scanning.Snapshots;

[TestClass]
public sealed class LruSnapshotStorageTests
{
    [TestMethod]
    public async Task MemoryPreferredSnapshotIsCachedAndReportsUsage()
    {
        using var fixture = new CacheFixture(
            CreatePolicy(maximumCachedNodes: 3));
        var sessionId = Guid.NewGuid();
        var snapshot = await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            count: 3);

        var page = await fixture.Cache.ReadPageAsync(
            snapshot,
            pageNumber: 2,
            pageSize: 2);
        var usage = await fixture.Cache.GetUsageAsync(sessionId);

        Assert.IsTrue(page.IsSuccess);
        Assert.AreEqual(1, page.Value.Items.Count);
        Assert.AreEqual(
            2,
            BinaryPrimitives.ReadInt32LittleEndian(
                page.Value.Items[0].Value.Span));
        Assert.IsTrue(usage.IsSuccess);
        Assert.AreEqual(36L, usage.Value.MemoryBytes);
        Assert.AreEqual(1, usage.Value.CachedNodeCount);
        Assert.AreEqual(3L, usage.Value.CachedRecordCount);
        Assert.IsTrue(usage.Value.DiskBytes > 0);
        Assert.IsTrue(usage.Value.CacheHits > 0);
    }

    [TestMethod]
    public async Task LeastRecentlyUsedNodeIsEvictedAtNodeLimit()
    {
        using var fixture = new CacheFixture(
            CreatePolicy(maximumCachedNodes: 2));
        var sessionId = Guid.NewGuid();
        var first = await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            count: 2);
        await fixture.WriteAsync(
            sessionId,
            nodeId: 2,
            count: 2);
        await fixture.Cache.ReadPageAsync(
            first,
            pageNumber: 1,
            pageSize: 1);
        await fixture.WriteAsync(
            sessionId,
            nodeId: 3,
            count: 2);

        var cached = fixture.Cache.GetCachedNodes();
        var usage = await fixture.Cache.GetUsageAsync(sessionId);

        CollectionAssert.AreEqual(
            new[] { 3, 1 },
            cached.Select(entry => entry.NodeId).ToArray());
        Assert.AreEqual(2, usage.Value.CachedNodeCount);
        Assert.AreEqual(1L, usage.Value.EvictionCount);
    }

    [TestMethod]
    public async Task LoweringBudgetImmediatelyEvictsLeastRecentNodes()
    {
        using var fixture = new CacheFixture(
            CreatePolicy(
                maximumCachedNodes: 3,
                memoryBudgetBytes: 1_000));
        var sessionId = Guid.NewGuid();
        await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            count: 2);
        await fixture.WriteAsync(
            sessionId,
            nodeId: 2,
            count: 2);

        var update = await fixture.Cache.UpdatePolicyAsync(
            CreatePolicy(
                maximumCachedNodes: 3,
                memoryBudgetBytes: 24),
            persist: true);

        Assert.IsTrue(update.IsSuccess);
        Assert.AreEqual(24L, update.Value.MemoryBytes);
        Assert.AreEqual(1, update.Value.CachedNodeCount);
        Assert.IsTrue(update.Value.MemoryBytes <=
                      update.Value.MemoryBudgetBytes);
        Assert.AreEqual(
            24L,
            fixture.Settings.SavedSettings!.MemoryBudgetBytes);
    }

    [TestMethod]
    public async Task DiskBackedThresholdBypassesWholeNodeCache()
    {
        using var fixture = new CacheFixture(
            new SnapshotCachePolicy(
                memoryBudgetBytes: 1_000,
                maximumCachedNodes: 3,
                pageSize: 2,
                memoryOnlyThreshold: 2,
                diskBackedThreshold: 3));
        var sessionId = Guid.NewGuid();
        var snapshot = await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            count: 3);

        var page = await fixture.Cache.ReadPageAsync(
            snapshot,
            pageNumber: 1,
            pageSize: 2);
        var usage = await fixture.Cache.GetUsageAsync(sessionId);

        Assert.IsTrue(page.IsSuccess);
        Assert.AreEqual(2, page.Value.Items.Count);
        Assert.AreEqual(0L, usage.Value.MemoryBytes);
        Assert.AreEqual(0, usage.Value.CachedNodeCount);
        Assert.IsTrue(usage.Value.DiskBytes > 0);
    }

    [TestMethod]
    public async Task HybridSnapshotIsCachedLazilyOnFirstRead()
    {
        using var fixture = new CacheFixture(
            new SnapshotCachePolicy(
                memoryBudgetBytes: 1_000,
                maximumCachedNodes: 3,
                pageSize: 2,
                memoryOnlyThreshold: 2,
                diskBackedThreshold: 100));
        var sessionId = Guid.NewGuid();
        var snapshot = await fixture.WriteAsync(
            sessionId,
            nodeId: 1,
            count: 3);
        var beforeRead =
            await fixture.Cache.GetUsageAsync(sessionId);

        var page = await fixture.Cache.ReadPageAsync(
            snapshot,
            pageNumber: 1,
            pageSize: 2);
        var afterRead =
            await fixture.Cache.GetUsageAsync(sessionId);

        Assert.AreEqual(0, beforeRead.Value.CachedNodeCount);
        Assert.IsTrue(page.IsSuccess);
        Assert.AreEqual(1, afterRead.Value.CachedNodeCount);
        Assert.AreEqual(36L, afterRead.Value.MemoryBytes);
    }

    [TestMethod]
    public async Task ByteBudgetKeepsManyBranchesWithinLimit()
    {
        using var fixture = new CacheFixture(
            CreatePolicy(
                maximumCachedNodes: 10,
                memoryBudgetBytes: 48));
        var sessionId = Guid.NewGuid();

        for (var nodeId = 1; nodeId <= 8; nodeId++)
        {
            await fixture.WriteAsync(
                sessionId,
                nodeId,
                count: 2);
        }

        var usage = await fixture.Cache.GetUsageAsync(sessionId);
        var cached = fixture.Cache.GetCachedNodes();

        Assert.IsTrue(usage.IsSuccess);
        Assert.AreEqual(48L, usage.Value.MemoryBytes);
        Assert.AreEqual(2, usage.Value.CachedNodeCount);
        Assert.IsTrue(
            usage.Value.MemoryBytes <=
            usage.Value.MemoryBudgetBytes);
        CollectionAssert.AreEqual(
            new[] { 8, 7 },
            cached.Select(entry => entry.NodeId).ToArray());
    }

    private static SnapshotCachePolicy CreatePolicy(
        int maximumCachedNodes,
        long memoryBudgetBytes = 1_000)
    {
        return new SnapshotCachePolicy(
            memoryBudgetBytes,
            maximumCachedNodes,
            pageSize: 2,
            memoryOnlyThreshold: 100,
            diskBackedThreshold: 1_000);
    }

    private sealed class CacheFixture : IDisposable
    {
        private readonly TemporaryDirectory _temporaryDirectory =
            new();
        private readonly BinarySnapshotStorage _storage;

        public CacheFixture(SnapshotCachePolicy policy)
        {
            var pathService = new AppPathService(
                _temporaryDirectory.RootPath);
            Settings = new StubSettingsService(
                new AppSettings
                {
                    MemoryBudgetBytes =
                        policy.MemoryBudgetBytes,
                    CachedNodeCount =
                        policy.MaximumCachedNodes,
                    PageSize = policy.PageSize,
                    MemoryOnlyThreshold =
                        policy.MemoryOnlyThreshold,
                    SnapshotThreshold =
                        policy.DiskBackedThreshold,
                });
            _storage = new BinarySnapshotStorage(
                pathService,
                TimeProvider.System);
            Cache = new LruSnapshotStorage(
                _storage,
                Settings,
                pathService,
                TimeProvider.System);
        }

        public StubSettingsService Settings { get; }

        public LruSnapshotStorage Cache { get; }

        public async Task<SnapshotDescriptor> WriteAsync(
            Guid sessionId,
            int nodeId,
            int count)
        {
            var result = await Cache.WriteAsync(
                new SnapshotWriteRequest(
                    sessionId,
                    nodeId,
                    ScanValueType.Int32,
                    includeValues: true,
                    expectedRecordCount: count),
                Records(count));

            Assert.IsTrue(
                result.IsSuccess,
                result.IsFailure
                    ? result.Error.ToDisplayMessage()
                    : null);
            return result.Value;
        }

        public void Dispose()
        {
            Cache.Dispose();
            _storage.Dispose();
            _temporaryDirectory.Dispose();
        }
    }

    private sealed class StubSettingsService(
        AppSettings settings) : ISettingsService
    {
        public AppSettings CurrentSettings { get; private set; } =
            settings;

        public AppSettings? SavedSettings { get; private set; }

        public Task<Result<AppSettings>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Result<AppSettings>.Success(
                    CurrentSettings));
        }

        public Task<Result> SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            CurrentSettings = settings;
            SavedSettings = settings;
            return Task.FromResult(Result.Success());
        }
    }

    private static async IAsyncEnumerable<SnapshotRecord>
        Records(
            int count,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(
                value,
                index);
            yield return new SnapshotRecord(
                new CandidateAddress(
                    checked((ulong)(0x1_000 + index))),
                value);
            await Task.Yield();
        }
    }
}

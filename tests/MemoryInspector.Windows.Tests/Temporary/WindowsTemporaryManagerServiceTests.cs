using System.Diagnostics;
using System.Runtime.CompilerServices;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.History;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Scanning.History;
using MemoryInspector.Windows.Scanning.Snapshots;
using MemoryInspector.Windows.Temporary;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.Temporary;

[TestClass]
public sealed class WindowsTemporaryManagerServiceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task InspectReportsSessionSnapshotAndDiskUsage()
    {
        using var fixture = new Fixture();
        var snapshot = await fixture.WriteSnapshotAsync(
            Guid.NewGuid(),
            nodeId: 1);

        var result = await fixture.Manager.InspectAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Statistics.SessionCount);
        Assert.AreEqual(1, result.Value.Statistics.SnapshotCount);
        Assert.IsTrue(result.Value.Statistics.TotalBytes > 0);
        Assert.AreEqual(snapshot.SessionId,
            result.Value.Sessions.Single().SessionId);
        Assert.IsFalse(
            result.Value.Sessions.Single().HasReadableHistory);
    }

    [TestMethod]
    public async Task AutomaticCleanupRemovesIncompleteFiles()
    {
        using var fixture = new Fixture(retentionDays: 36500);
        var sessionId = Guid.NewGuid();
        var snapshot = await fixture.WriteSnapshotAsync(
            sessionId,
            nodeId: 1);
        var incompletePath = Path.Combine(
            Path.GetDirectoryName(snapshot.FilePath)!,
            "tree.json.tmp-crash");
        await File.WriteAllTextAsync(incompletePath, "{}");

        var result =
            await fixture.Manager.RunAutomaticCleanupAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(File.Exists(incompletePath));
        Assert.IsTrue(File.Exists(snapshot.FilePath));
        Assert.AreEqual(
            1,
            result.Value.DiscardedIncompleteFileCount);
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(30_000)]
    public async Task AutomaticCleanupRemovesManyIncompleteFilesQuickly()
    {
        const int incompleteFileCount = 1_000;
        using var fixture = new Fixture(retentionDays: 36500);
        var snapshot = await fixture.WriteSnapshotAsync(
            Guid.NewGuid(),
            nodeId: 1);
        var sessionDirectory = Path.GetDirectoryName(
            snapshot.FilePath)!;

        for (var index = 0;
             index < incompleteFileCount;
             index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(
                    sessionDirectory,
                    $"tree-{index}.json.tmp-crash"),
                "{}");
        }

        var timer = Stopwatch.StartNew();
        var result =
            await fixture.Manager.RunAutomaticCleanupAsync();
        timer.Stop();
        var filesPerSecond =
            incompleteFileCount / timer.Elapsed.TotalSeconds;
        TestContext.WriteLine(
            $"METRIC temp_cleanup_files_per_second=" +
            $"{filesPerSecond:F0}");
        Console.WriteLine(
            $"METRIC temp_cleanup_files_per_second=" +
            $"{filesPerSecond:F0}");
        TestContext.WriteLine(
            $"METRIC temp_cleanup_milliseconds=" +
            $"{timer.Elapsed.TotalMilliseconds:F1}");
        Console.WriteLine(
            $"METRIC temp_cleanup_milliseconds=" +
            $"{timer.Elapsed.TotalMilliseconds:F1}");

        Assert.IsTrue(
            result.IsSuccess,
            result.IsFailure
                ? result.Error.ToDisplayMessage()
                : null);
        Assert.AreEqual(
            incompleteFileCount,
            result.Value.DiscardedIncompleteFileCount);
        Assert.IsTrue(File.Exists(snapshot.FilePath));
        Assert.IsFalse(
            Directory.EnumerateFiles(
                sessionDirectory,
                "*.tmp-crash",
                SearchOption.TopDirectoryOnly).Any());
        Assert.IsTrue(
            timer.Elapsed < TimeSpan.FromSeconds(30),
            $"Temporary cleanup took {timer.Elapsed.TotalSeconds:F2}s.");
    }

    [TestMethod]
    public async Task AutomaticCleanupDeletesExpiredUnpinnedSession()
    {
        using var fixture = new Fixture(retentionDays: 7);
        var sessionId = Guid.NewGuid();
        var snapshot = await fixture.WriteSnapshotAsync(
            sessionId,
            nodeId: 1);
        await fixture.SaveRootHistoryAsync(snapshot);
        var directory = Path.GetDirectoryName(
            snapshot.FilePath)!;
        Directory.SetLastWriteTimeUtc(
            directory,
            DateTime.UtcNow.AddDays(-30));

        var result =
            await fixture.Manager.RunAutomaticCleanupAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.DeletedSessionCount);
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public async Task PinnedSessionIsRetainedUnlessExplicitlyIncluded()
    {
        using var fixture = new Fixture();
        var sessionId = Guid.NewGuid();
        var snapshot = await fixture.WriteSnapshotAsync(
            sessionId,
            nodeId: 1);
        await fixture.SaveRootHistoryAsync(
            snapshot,
            isPinned: true);

        var retained = await fixture.Manager.DeleteSessionAsync(
            sessionId);
        var deleted = await fixture.Manager.DeleteSessionAsync(
            sessionId,
            includePinned: true);

        Assert.IsTrue(retained.IsSuccess);
        Assert.AreEqual(
            1,
            retained.Value.RetainedPinnedSessionCount);
        Assert.IsTrue(deleted.IsSuccess);
        Assert.AreEqual(1, deleted.Value.DeletedSessionCount);
        Assert.IsFalse(Directory.Exists(
            Path.GetDirectoryName(snapshot.FilePath)!));
    }

    [TestMethod]
    public async Task DeleteSessionDoesNotAffectSavedAddresses()
    {
        using var fixture = new Fixture();
        var sessionId = Guid.NewGuid();
        _ = await fixture.WriteSnapshotAsync(
            sessionId,
            nodeId: 1);
        var savedPath = Path.Combine(
            fixture.Paths.SavedAddressesDirectory,
            "catalog.json");
        await File.WriteAllTextAsync(savedPath, """{"entries":[]}""");

        var result = await fixture.Manager.DeleteSessionAsync(
            sessionId,
            includePinned: true);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(File.Exists(savedPath));
        Assert.AreEqual(
            """{"entries":[]}""",
            await File.ReadAllTextAsync(savedPath));
    }

    [TestMethod]
    public async Task CompactDeletesOrphanAndKeepsTreeReadable()
    {
        using var fixture = new Fixture();
        var sessionId = Guid.NewGuid();
        var retained = await fixture.WriteSnapshotAsync(
            sessionId,
            nodeId: 1);
        var orphan = await fixture.WriteSnapshotAsync(
            sessionId,
            nodeId: 2);
        await fixture.SaveRootHistoryAsync(retained);

        var result = await fixture.Manager.CompactSessionAsync(
            sessionId);
        var history = await fixture.History.LoadAsync(sessionId);
        var opened = await fixture.Storage.OpenAsync(
            sessionId,
            nodeId: 1);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.CompactedSessionCount);
        Assert.AreEqual(1, result.Value.DeletedSnapshotCount);
        Assert.IsFalse(File.Exists(orphan.FilePath));
        Assert.IsTrue(history.IsSuccess);
        Assert.AreEqual(1, history.Value.Rounds.Count);
        Assert.IsTrue(opened.IsSuccess);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory _temporaryDirectory =
            new();
        private readonly BinarySnapshotStorage _storage;
        private readonly JsonScanHistoryStore _history;

        public Fixture(int retentionDays = 7)
        {
            Paths = new AppPathService(
                _temporaryDirectory.RootPath);
            Assert.IsTrue(Paths.EnsureDirectories().IsSuccess);
            _storage = new BinarySnapshotStorage(
                Paths,
                TimeProvider.System);
            _history = new JsonScanHistoryStore(Paths);
            Storage = _storage;
            History = _history;
            Manager = new WindowsTemporaryManagerService(
                Paths,
                new StubSettingsService(retentionDays),
                new StubPipelineService(),
                _history,
                _storage,
                new StubCacheManager(),
                TimeProvider.System);
        }

        public AppPathService Paths { get; }

        public BinarySnapshotStorage Storage { get; }

        public JsonScanHistoryStore History { get; }

        public WindowsTemporaryManagerService Manager { get; }

        public async Task<SnapshotDescriptor> WriteSnapshotAsync(
            Guid sessionId,
            int nodeId)
        {
            var result = await _storage.WriteAsync(
                new SnapshotWriteRequest(
                    sessionId,
                    nodeId,
                    ScanValueType.Int32,
                    includeValues: true,
                    expectedRecordCount: 2),
                Records());
            Assert.IsTrue(result.IsSuccess);
            return result.Value;
        }

        public async Task SaveRootHistoryAsync(
            SnapshotDescriptor snapshot,
            bool isPinned = false)
        {
            var roundId = Guid.NewGuid();
            var round = new ScanHistoryRoundRecord(
                roundId,
                parentRoundId: null,
                roundNumber: 0,
                name: "Initial",
                isPinned,
                operationKind: null,
                comparisonMode: null,
                input: null,
                beforeCount: snapshot.RecordCount,
                afterCount: snapshot.RecordCount,
                snapshot.CreatedAt,
                startedAt: null,
                completedAt: null,
                isPartial: false,
                warningCount: 0,
                suppressedWarningCount: 0,
                observationDurationTicks: null,
                observationMode: null,
                snapshot.NodeId,
                snapshot.ValueType,
                snapshot.RecordCount,
                snapshot.Checksum,
                snapshot.FilePath,
                snapshot.StorageKind,
                snapshot.ParentNodeId,
                snapshot.ChainDepth,
                snapshot.AccumulatedDeltaBytes);
            var save = await _history.SaveAsync(
                new ScanHistoryDocument(
                    ScanHistoryDocument.CurrentFormatVersion,
                    snapshot.SessionId,
                    roundId,
                    pendingRoundId: null,
                    [round]));
            Assert.IsTrue(save.IsSuccess);
        }

        public void Dispose()
        {
            Manager.Dispose();
            _history.Dispose();
            _storage.Dispose();
            _temporaryDirectory.Dispose();
        }

        private static async IAsyncEnumerable<SnapshotRecord>
            Records(
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < 2; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new SnapshotRecord(
                    new CandidateAddress(
                        0x1000UL + (ulong)(index * 4)),
                    BitConverter.GetBytes(index));
                await Task.Yield();
            }
        }
    }

    private sealed class StubSettingsService(int retentionDays) :
        ISettingsService
    {
        private readonly AppSettings _settings =
            AppSettings.CreateDefault() with
            {
                TempRetentionDays = retentionDays,
            };

        public Task<Result<AppSettings>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Result<AppSettings>.Success(_settings));
        }

        public Task<Result> SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class StubCacheManager :
        ISnapshotCacheManager
    {
        public SnapshotCachePolicy CurrentPolicy { get; } = new();

        public IReadOnlyList<SnapshotCacheEntryInfo>
            GetCachedNodes() => [];

        public Task<Result<SnapshotCacheUsage>> GetUsageAsync(
            Guid? sessionId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Result<SnapshotCacheUsage>.Success(
                    new SnapshotCacheUsage(
                        MemoryBytes: 0,
                        MemoryBudgetBytes: 1,
                        CachedNodeCount: 0,
                        MaximumCachedNodes: 1,
                        CachedRecordCount: 0,
                        DiskBytes: 0,
                        CacheHits: 0,
                        CacheMisses: 0,
                        EvictionCount: 0)));
        }

        public Task<Result<SnapshotCacheUsage>> UpdatePolicyAsync(
            SnapshotCachePolicy policy,
            bool persist = true,
            CancellationToken cancellationToken = default)
        {
            return GetUsageAsync(
                cancellationToken: cancellationToken);
        }

        public Result Clear(Guid? sessionId = null)
        {
            return Result.Success();
        }
    }

    private sealed class StubPipelineService :
        IFilterPipelineService
    {
        public FilterPipelineState? CurrentState => null;

        public Result CloseSession(Guid sessionId) =>
            Result.Success();

        public Task<Result<FilterPipelineState>> StartAsync(
            SnapshotDescriptor initialSnapshot,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> LoadAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<PendingFilterResult>> RunNextScanAsync(
            ScanRequest filter,
            int pageSize = NextScanRequest.DefaultPageSize,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<PendingFilterResult>>
            RunDurationFilterAsync(
                ScanRequest filter,
                TimeSpan duration,
                DurationFilterObservationMode observationMode,
                TimeSpan? sampleInterval = null,
                int pageSize =
                    DurationFilterRequest.DefaultPageSize,
                DurationFilterExecutionControl? executionControl =
                    null,
                IProgress<OperationProgress>? progress = null,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> KeepResultAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> DiscardResultAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> UndoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> RedoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> RenameRoundAsync(
            Guid roundId,
            string name,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>>
            DeletePendingRoundAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> BranchFromAsync(
            Guid nodeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> SetActiveNodeAsync(
            Guid nodeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> RenameNodeAsync(
            Guid nodeId,
            string name,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>>
            SetNodePinnedAsync(
                Guid nodeId,
                bool isPinned,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> DeleteBranchAsync(
            Guid nodeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Result<ScanTreeNodeComparison> CompareNodes(
            Guid leftNodeId,
            Guid rightNodeId) =>
            throw new NotSupportedException();

        public Result<IReadOnlyList<ScanTreeNode>>
            GetChildNodes(Guid nodeId) =>
            throw new NotSupportedException();

        public Result<IReadOnlyList<ScanTreeNode>>
            GetPathToRoot(Guid nodeId) =>
            throw new NotSupportedException();
    }
}

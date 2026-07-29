using System.Diagnostics;
using System.Runtime.CompilerServices;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.History;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Scanning.History;
using MemoryInspector.Windows.Scanning.Snapshots;

namespace MemoryInspector.IntegrationTests.Scanning;

[TestClass]
public sealed class FilterPipelineServiceTests
{
    private const ulong BaseAddress = 0x3_000;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RunsUnchangedChangedAndIncreasedInSequence()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20,
            30,
            40);
        Assert.IsTrue(
            (await fixture.Pipeline.StartAsync(initial)).IsSuccess);

        fixture.Reader.SetValues(10, 25, 30, 50);
        var unchanged = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));

        AssertPending(
            unchanged,
            beforeCount: 4,
            afterCount: 2,
            activeRound: 0);
        Assert.AreEqual(
            "Unchanged: 4 → 2",
            unchanged.Value.Summary.DisplayText);
        Assert.IsFalse(
            fixture.Pipeline.CurrentState!.CanContinueFiltering);

        var blocked = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Changed));

        Assert.IsTrue(blocked.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, blocked.Error.Code);
        Assert.IsTrue(
            (await fixture.Pipeline.KeepResultAsync()).IsSuccess);

        fixture.Reader.SetValues(11, 25, 30, 50);
        var changed = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Changed));

        AssertPending(
            changed,
            beforeCount: 2,
            afterCount: 1,
            activeRound: 1);
        Assert.IsTrue(
            (await fixture.Pipeline.KeepResultAsync()).IsSuccess);

        fixture.Reader.SetValues(12, 25, 30, 50);
        var increased = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Increased));

        AssertPending(
            increased,
            beforeCount: 1,
            afterCount: 1,
            activeRound: 2);
        var kept = await fixture.Pipeline.KeepResultAsync();

        Assert.IsTrue(kept.IsSuccess);
        Assert.AreEqual(3L, kept.Value.ActiveRound.RoundNumber);
        Assert.AreEqual(1L, kept.Value.CurrentCandidateCount);
        Assert.IsNull(kept.Value.PendingResult);
        Assert.IsTrue(kept.Value.CanContinueFiltering);
    }

    [TestMethod]
    public async Task DiscardReturnsToParentAndAllowsAnotherFilter()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20);
        await fixture.Pipeline.StartAsync(initial);
        fixture.Reader.SetValues(11, 20);
        var pending = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Changed));

        Assert.AreEqual(1L, pending.Value.AfterCount);

        var discarded =
            await fixture.Pipeline.DiscardResultAsync();

        Assert.IsTrue(discarded.IsSuccess);
        Assert.AreEqual(0L, discarded.Value.ActiveRound.RoundNumber);
        Assert.AreEqual(2L, discarded.Value.CurrentCandidateCount);
        Assert.IsNull(discarded.Value.PendingResult);

        fixture.Reader.SetValues(10, 20);
        var replacement = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));

        Assert.IsTrue(replacement.IsSuccess);
        Assert.AreEqual(2L, replacement.Value.BeforeCount);
        Assert.AreEqual(2L, replacement.Value.AfterCount);
        Assert.AreEqual(3, replacement.Value.Round.Snapshot.NodeId);
    }

    [TestMethod]
    public async Task DurationFilterProducesPendingSummary()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20);
        await fixture.Pipeline.StartAsync(initial);
        fixture.Reader.SetValues(10, 25);

        var pending =
            await fixture.Pipeline.RunDurationFilterAsync(
                CreateFilter(ScanComparisonMode.Changed),
                duration: TimeSpan.FromMilliseconds(20),
                DurationFilterObservationMode.EndpointCompare,
                sampleInterval: TimeSpan.FromMilliseconds(10));

        Assert.IsTrue(pending.IsSuccess);
        Assert.AreEqual(2L, pending.Value.BeforeCount);
        Assert.AreEqual(1L, pending.Value.AfterCount);
        Assert.AreEqual(
            FilterPipelineOperationKind.DurationFilter,
            pending.Value.Summary.OperationKind);
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(20),
            pending.Value.Summary.ObservationDuration);
        Assert.AreEqual(
            DurationFilterObservationMode.EndpointCompare,
            pending.Value.Summary.ObservationMode);
    }

    [TestMethod]
    public async Task NodeAllocationSkipsExistingSnapshots()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10);
        _ = await fixture.WriteSnapshotAsync(
            nodeId: 2,
            99);
        await fixture.Pipeline.StartAsync(initial);
        fixture.Reader.SetValues(10);

        var pending = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));

        Assert.IsTrue(pending.IsSuccess);
        Assert.AreEqual(3, pending.Value.Round.Snapshot.NodeId);
    }

    [TestMethod]
    public async Task InvalidLifecycleActionsReturnInvalidState()
    {
        using var fixture = new PipelineFixture();

        var beforeStart = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));
        var keepBeforeStart =
            await fixture.Pipeline.KeepResultAsync();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10);
        var started = await fixture.Pipeline.StartAsync(initial);
        var keepWithoutPending =
            await fixture.Pipeline.KeepResultAsync();
        var discardWithoutPending =
            await fixture.Pipeline.DiscardResultAsync();

        Assert.AreEqual(
            ErrorCode.InvalidState,
            beforeStart.Error.Code);
        Assert.AreEqual(
            ErrorCode.InvalidState,
            keepBeforeStart.Error.Code);
        Assert.IsTrue(started.IsSuccess);
        Assert.AreEqual(
            ErrorCode.InvalidState,
            keepWithoutPending.Error.Code);
        Assert.AreEqual(
            ErrorCode.InvalidState,
            discardWithoutPending.Error.Code);
    }

    [TestMethod]
    public async Task FailedFilterLeavesActiveRoundUnchanged()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10);
        await fixture.Pipeline.StartAsync(initial);
        fixture.Reader.Failure = new Error(
            ErrorCode.NativeApi,
            "The target read failed.");

        var result = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(
            ErrorCode.NativeApi,
            result.Error.Code);
        Assert.AreEqual(
            0L,
            fixture.Pipeline.CurrentState!
                .ActiveRound.RoundNumber);
        Assert.IsNull(
            fixture.Pipeline.CurrentState.PendingResult);
        Assert.IsTrue(
            fixture.Pipeline.CurrentState
                .CanContinueFiltering);
    }

    [TestMethod]
    public async Task StartRejectsSnapshotFromAnotherSession()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10);
        fixture.SessionService.CurrentSession =
            fixture.SessionService.CurrentSession! with
            {
                SessionId = Guid.NewGuid(),
            };

        var result = await fixture.Pipeline.StartAsync(initial);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.IsNull(fixture.Pipeline.CurrentState);
    }

    [TestMethod]
    public async Task UndoAndRedoReuseSnapshotsWithoutReadingProcess()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20,
            30);
        await fixture.Pipeline.StartAsync(initial);
        fixture.Reader.SetValues(10, 25, 30);
        var pending = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));
        await fixture.Pipeline.KeepResultAsync();
        var readsBeforeUndo = fixture.Reader.BatchCallCount;

        var undone = await fixture.Pipeline.UndoAsync();

        Assert.IsTrue(undone.IsSuccess);
        Assert.AreEqual(0L, undone.Value.ActiveRound.RoundNumber);
        Assert.AreEqual(
            3L,
            undone.Value.ActiveRound.CandidateCount);
        Assert.AreEqual(2L, undone.Value.CurrentCandidateCount);
        Assert.AreEqual(
            pending.Value.Round.RoundId,
            undone.Value.PendingResult!.Round.RoundId);
        Assert.IsTrue(undone.Value.CanRedo);
        Assert.AreEqual(
            readsBeforeUndo,
            fixture.Reader.BatchCallCount);

        var redone = await fixture.Pipeline.RedoAsync();

        Assert.IsTrue(redone.IsSuccess);
        Assert.AreEqual(1L, redone.Value.ActiveRound.RoundNumber);
        Assert.AreEqual(2L, redone.Value.CurrentCandidateCount);
        Assert.IsNull(redone.Value.PendingResult);
        Assert.AreEqual(
            readsBeforeUndo,
            fixture.Reader.BatchCallCount);
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(60_000)]
    public async Task FilterPipelineThroughputMeetsBaseline()
    {
        const int candidateCount = 25_000;
        using var fixture = new PipelineFixture();
        var values = Enumerable.Range(0, candidateCount).ToArray();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            values);
        Assert.IsTrue(
            (await fixture.Pipeline.StartAsync(initial)).IsSuccess);
        fixture.Reader.SetValues(values);
        var timer = Stopwatch.StartNew();

        var filtered = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));

        timer.Stop();
        var candidatesPerSecond =
            candidateCount / timer.Elapsed.TotalSeconds;
        TestContext.WriteLine(
            $"METRIC filter_candidates_per_second=" +
            $"{candidatesPerSecond:F0}");
        Console.WriteLine(
            $"METRIC filter_candidates_per_second=" +
            $"{candidatesPerSecond:F0}");
        TestContext.WriteLine(
            $"METRIC filter_elapsed_milliseconds=" +
            $"{timer.Elapsed.TotalMilliseconds:F1}");
        Console.WriteLine(
            $"METRIC filter_elapsed_milliseconds=" +
            $"{timer.Elapsed.TotalMilliseconds:F1}");

        Assert.IsTrue(
            filtered.IsSuccess,
            filtered.IsFailure
                ? filtered.Error.ToDisplayMessage()
                : null);
        Assert.AreEqual(
            candidateCount,
            filtered.Value.AfterCount);
        Assert.IsTrue(
            candidatesPerSecond >= 1_000,
            $"Filter throughput was {candidatesPerSecond:F0} candidates/s.");
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(60_000)]
    public async Task RepeatedUndoRedoAndBranchingReuseStableHistory()
    {
        const int undoRedoCycles = 100;
        const int branchCount = 20;
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20,
            30,
            40);
        var started = await fixture.Pipeline.StartAsync(initial);
        var root = started.Value.ActiveRound;
        fixture.Reader.SetValues(10, 20, 30, 40);
        Assert.IsTrue(
            (await fixture.Pipeline.RunNextScanAsync(
                CreateFilter(
                    ScanComparisonMode.Unchanged))).IsSuccess);
        Assert.IsTrue(
            (await fixture.Pipeline.KeepResultAsync()).IsSuccess);
        var readsBeforeNavigation =
            fixture.Reader.BatchCallCount;
        var navigationTimer = Stopwatch.StartNew();

        for (var index = 0; index < undoRedoCycles; index++)
        {
            Assert.IsTrue(
                (await fixture.Pipeline.UndoAsync()).IsSuccess);
            Assert.IsTrue(
                (await fixture.Pipeline.RedoAsync()).IsSuccess);
        }

        navigationTimer.Stop();
        Assert.AreEqual(
            readsBeforeNavigation,
            fixture.Reader.BatchCallCount);
        var branchTimer = Stopwatch.StartNew();

        for (var index = 0; index < branchCount; index++)
        {
            Assert.IsTrue(
                (await fixture.Pipeline.BranchFromAsync(
                    root.RoundId)).IsSuccess);
            fixture.Reader.SetValues(
                11 + index,
                20,
                30,
                40);
            Assert.IsTrue(
                (await fixture.Pipeline.RunNextScanAsync(
                    CreateFilter(
                        ScanComparisonMode.Changed))).IsSuccess);
            Assert.IsTrue(
                (await fixture.Pipeline.KeepResultAsync()).IsSuccess);
        }

        branchTimer.Stop();
        var children = fixture.Pipeline.GetChildNodes(
            root.RoundId);
        Assert.IsTrue(children.IsSuccess);
        Assert.AreEqual(
            branchCount + 1,
            children.Value.Count);
        TestContext.WriteLine(
            $"METRIC undo_redo_cycles_per_second=" +
            $"{undoRedoCycles / navigationTimer.Elapsed.TotalSeconds:F0}");
        Console.WriteLine(
            $"METRIC undo_redo_cycles_per_second=" +
            $"{undoRedoCycles / navigationTimer.Elapsed.TotalSeconds:F0}");
        TestContext.WriteLine(
            $"METRIC branches_per_second=" +
            $"{branchCount / branchTimer.Elapsed.TotalSeconds:F1}");
        Console.WriteLine(
            $"METRIC branches_per_second=" +
            $"{branchCount / branchTimer.Elapsed.TotalSeconds:F1}");
    }

    [TestMethod]
    public async Task HistoryReloadsRoundMetadataAfterRestart()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20);
        await fixture.Pipeline.StartAsync(initial);
        fixture.Reader.SetValues(11, 20);
        var pending = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Changed));
        await fixture.Pipeline.KeepResultAsync();
        await fixture.Pipeline.RenameRoundAsync(
            pending.Value.Round.RoundId,
            "Changed values");
        fixture.RestartPipeline();

        var restored = await fixture.Pipeline.LoadAsync(
            fixture.SessionService.CurrentSession!.SessionId);

        Assert.IsTrue(
            restored.IsSuccess,
            restored.Error.Exception?.ToString() ??
            restored.Error.ToDisplayMessage());
        Assert.AreEqual(2, restored.Value.Rounds.Count);
        Assert.AreEqual(
            "Changed values",
            restored.Value.ActiveRound.Name);
        Assert.AreEqual(
            pending.Value.Round.RoundId,
            restored.Value.ActiveRound.RoundId);
        Assert.AreEqual(
            restored.Value.Rounds[0].RoundId,
            restored.Value.ActiveRound.ParentRoundId);
        Assert.AreEqual(
            ScanComparisonMode.Changed,
            restored.Value.ActiveRound.Input!.ComparisonMode);
        Assert.AreEqual(
            ScanValueType.Int32,
            restored.Value.ActiveRound.Input.ValueType);
        Assert.AreEqual(
            2L,
            restored.Value.ActiveRound.Summary!.BeforeCount);
        Assert.AreEqual(
            1L,
            restored.Value.ActiveRound.Summary.AfterCount);
        Assert.IsTrue(
            File.Exists(
                restored.Value.ActiveRound.StorageReference));
    }

    [TestMethod]
    public async Task PendingRoundReloadsAndCanBeRedone()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20);
        await fixture.Pipeline.StartAsync(initial);
        fixture.Reader.SetValues(11, 20);
        var pending = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Changed));
        fixture.RestartPipeline();

        var restored = await fixture.Pipeline.LoadAsync(
            fixture.SessionService.CurrentSession!.SessionId);
        var redone = await fixture.Pipeline.RedoAsync();

        Assert.IsTrue(
            restored.IsSuccess,
            restored.Error.ToDisplayMessage());
        Assert.AreEqual(
            pending.Value.Round.RoundId,
            restored.Value.PendingResult!.Round.RoundId);
        Assert.AreEqual(1L, restored.Value.CurrentCandidateCount);
        Assert.IsTrue(redone.IsSuccess);
        Assert.AreEqual(
            pending.Value.Round.RoundId,
            redone.Value.ActiveRound.RoundId);
    }

    [TestMethod]
    public async Task DeletePendingRoundRemovesMetadataAndSnapshot()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20);
        await fixture.Pipeline.StartAsync(initial);
        fixture.Reader.SetValues(11, 20);
        var pending = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Changed));
        var snapshotPath = pending.Value.Round.StorageReference;

        var deleted =
            await fixture.Pipeline.DeletePendingRoundAsync();

        Assert.IsTrue(deleted.IsSuccess);
        Assert.IsFalse(File.Exists(snapshotPath));
        Assert.AreEqual(1, deleted.Value.Rounds.Count);
        Assert.IsNull(deleted.Value.PendingResult);

        fixture.RestartPipeline();
        var restored = await fixture.Pipeline.LoadAsync(
            fixture.SessionService.CurrentSession!.SessionId);

        Assert.IsTrue(
            restored.IsSuccess,
            restored.Error.ToDisplayMessage());
        Assert.AreEqual(1, restored.Value.Rounds.Count);
        Assert.IsNull(restored.Value.PendingResult);
    }

    [TestMethod]
    public async Task UndoRequiresKeptRoundAndNoPendingResult()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10);
        await fixture.Pipeline.StartAsync(initial);

        var initialUndo = await fixture.Pipeline.UndoAsync();

        fixture.Reader.SetValues(10);
        await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));
        var pendingUndo = await fixture.Pipeline.UndoAsync();

        Assert.AreEqual(
            ErrorCode.InvalidState,
            initialUndo.Error.Code);
        Assert.AreEqual(
            ErrorCode.InvalidState,
            pendingUndo.Error.Code);
    }

    [TestMethod]
    public async Task CorruptedHistoryIsRejectedWithoutChangingState()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10);
        await fixture.Pipeline.StartAsync(initial);
        fixture.CorruptHistoryAndRestart();

        var result = await fixture.Pipeline.LoadAsync(
            fixture.SessionService.CurrentSession!.SessionId);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(
            ErrorCode.Serialization,
            result.Error.Code);
        Assert.IsNull(fixture.Pipeline.CurrentState);
    }

    [TestMethod]
    public async Task BranchesFromHistoricalNodeAndContinuesFiltering()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20,
            30,
            40);
        var started = await fixture.Pipeline.StartAsync(initial);
        var root = started.Value.ActiveRound;
        fixture.Reader.SetValues(10, 25, 30, 40);
        var firstBranch =
            await fixture.Pipeline.RunNextScanAsync(
                CreateFilter(ScanComparisonMode.Unchanged));
        await fixture.Pipeline.KeepResultAsync();
        var snapshotCountBefore =
            fixture.SnapshotFileCount();
        var readsBefore = fixture.Reader.BatchCallCount;

        var branched = await fixture.Pipeline.BranchFromAsync(
            root.RoundId);

        Assert.IsTrue(branched.IsSuccess);
        Assert.AreEqual(root.RoundId, branched.Value.ActiveRound.RoundId);
        Assert.AreEqual(
            snapshotCountBefore,
            fixture.SnapshotFileCount());
        Assert.AreEqual(readsBefore, fixture.Reader.BatchCallCount);

        fixture.Reader.SetValues(11, 20, 30, 50);
        var secondBranch =
            await fixture.Pipeline.RunNextScanAsync(
                CreateFilter(ScanComparisonMode.Changed));
        await fixture.Pipeline.KeepResultAsync();
        var children = fixture.Pipeline.GetChildNodes(
            root.RoundId);

        Assert.IsTrue(secondBranch.IsSuccess);
        Assert.AreEqual(4L, secondBranch.Value.BeforeCount);
        Assert.AreEqual(2L, secondBranch.Value.AfterCount);
        Assert.AreEqual(2, children.Value.Count);
        CollectionAssert.AreEquivalent(
            new[]
            {
                firstBranch.Value.Round.RoundId,
                secondBranch.Value.Round.RoundId,
            },
            children.Value
                .Select(node => node.NodeId)
                .ToArray());

        fixture.Reader.SetValues(12, 20, 30, 50);
        var continued =
            await fixture.Pipeline.RunNextScanAsync(
                CreateFilter(ScanComparisonMode.Increased));

        Assert.AreEqual(
            secondBranch.Value.Round.RoundId,
            continued.Value.Parent.RoundId);
        Assert.AreEqual(2L, continued.Value.BeforeCount);
        Assert.AreEqual(1L, continued.Value.AfterCount);
    }

    [TestMethod]
    public async Task TreeNavigationComparisonAndActiveNodeSurviveRestart()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20,
            30);
        var started = await fixture.Pipeline.StartAsync(initial);
        var root = started.Value.ActiveRound;
        fixture.Reader.SetValues(10, 20, 30);
        var left = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));
        await fixture.Pipeline.KeepResultAsync();
        await fixture.Pipeline.BranchFromAsync(root.RoundId);
        fixture.Reader.SetValues(11, 20, 30);
        var right = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Changed));
        await fixture.Pipeline.KeepResultAsync();
        await fixture.Pipeline.RenameNodeAsync(
            right.Value.Round.RoundId,
            "Right branch");
        await fixture.Pipeline.SetNodePinnedAsync(
            left.Value.Round.RoundId,
            isPinned: true);

        var siblingComparison = fixture.Pipeline.CompareNodes(
            left.Value.Round.RoundId,
            right.Value.Round.RoundId);
        var ancestorComparison = fixture.Pipeline.CompareNodes(
            root.RoundId,
            right.Value.Round.RoundId);
        var path = fixture.Pipeline.GetPathToRoot(
            right.Value.Round.RoundId);

        Assert.IsTrue(siblingComparison.IsSuccess);
        Assert.AreEqual(
            root.RoundId,
            siblingComparison.Value.NearestCommonAncestorId);
        Assert.AreEqual(
            -2L,
            siblingComparison.Value.CandidateCountDelta);
        Assert.IsFalse(
            siblingComparison.Value.IsLeftAncestorOfRight);
        Assert.IsTrue(
            ancestorComparison.Value.IsLeftAncestorOfRight);
        CollectionAssert.AreEqual(
            new[]
            {
                root.RoundId,
                right.Value.Round.RoundId,
            },
            path.Value.Select(node => node.NodeId).ToArray());

        fixture.RestartPipeline();
        var restored = await fixture.Pipeline.LoadAsync(
            fixture.SessionService.CurrentSession!.SessionId);

        Assert.IsTrue(restored.IsSuccess);
        Assert.AreEqual(3, restored.Value.TreeNodes.Count);
        Assert.AreEqual(
            right.Value.Round.RoundId,
            restored.Value.ActiveRound.RoundId);
        Assert.AreEqual(
            1,
            restored.Value.TreeNodes.Count(node =>
                node.IsActive));
        Assert.AreEqual(
            "Right branch",
            restored.Value.ActiveRound.Name);
        Assert.AreEqual(
            ScanTreeStorageType.DeltaKeep,
            restored.Value.TreeNodes.Single(node =>
                node.IsActive).StorageType);
        Assert.IsTrue(restored.Value.TreeNodes.Single(node =>
            node.NodeId == left.Value.Round.RoundId).IsPinned);
    }

    [TestMethod]
    public async Task PinnedDescendantPreventsRecursiveBranchDeletion()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10,
            20);
        var started = await fixture.Pipeline.StartAsync(initial);
        var root = started.Value.ActiveRound;
        fixture.Reader.SetValues(10, 20);
        var branch = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));
        await fixture.Pipeline.KeepResultAsync();
        fixture.Reader.SetValues(11, 20);
        var descendant =
            await fixture.Pipeline.RunNextScanAsync(
                CreateFilter(ScanComparisonMode.Changed));
        await fixture.Pipeline.KeepResultAsync();
        await fixture.Pipeline.BranchFromAsync(root.RoundId);
        fixture.Reader.SetValues(10, 21);
        var retained = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Changed));
        await fixture.Pipeline.KeepResultAsync();
        await fixture.Pipeline.SetNodePinnedAsync(
            descendant.Value.Round.RoundId,
            isPinned: true);

        var blocked = await fixture.Pipeline.DeleteBranchAsync(
            branch.Value.Round.RoundId);

        Assert.AreEqual(ErrorCode.InvalidState, blocked.Error.Code);
        Assert.IsTrue(
            File.Exists(branch.Value.Round.StorageReference));
        Assert.IsTrue(
            File.Exists(descendant.Value.Round.StorageReference));

        await fixture.Pipeline.SetNodePinnedAsync(
            descendant.Value.Round.RoundId,
            isPinned: false);
        var deleted = await fixture.Pipeline.DeleteBranchAsync(
            branch.Value.Round.RoundId);

        Assert.IsTrue(deleted.IsSuccess);
        Assert.IsFalse(
            File.Exists(branch.Value.Round.StorageReference));
        Assert.IsFalse(
            File.Exists(descendant.Value.Round.StorageReference));
        Assert.AreEqual(2, deleted.Value.TreeNodes.Count);
        Assert.IsTrue(deleted.Value.TreeNodes.Any(node =>
            node.NodeId == retained.Value.Round.RoundId));
    }

    [TestMethod]
    public async Task RootActiveAndPendingNodesCannotBeDeletedAsBranch()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10);
        var started = await fixture.Pipeline.StartAsync(initial);
        var rootDelete = await fixture.Pipeline.DeleteBranchAsync(
            started.Value.ActiveRound.RoundId);
        fixture.Reader.SetValues(10);
        var pending = await fixture.Pipeline.RunNextScanAsync(
            CreateFilter(ScanComparisonMode.Unchanged));
        var switchWhilePending =
            await fixture.Pipeline.SetActiveNodeAsync(
                started.Value.ActiveRound.RoundId);
        await fixture.Pipeline.KeepResultAsync();
        var activeDelete =
            await fixture.Pipeline.DeleteBranchAsync(
                pending.Value.Round.RoundId);

        Assert.AreEqual(
            ErrorCode.InvalidState,
            rootDelete.Error.Code);
        Assert.AreEqual(
            ErrorCode.InvalidState,
            switchWhilePending.Error.Code);
        Assert.AreEqual(
            ErrorCode.InvalidState,
            activeDelete.Error.Code);
    }

    [TestMethod]
    public async Task LoadsLegacyLinearHistoryBeforeTreeMigration()
    {
        using var fixture = new PipelineFixture();
        var initial = await fixture.WriteSnapshotAsync(
            nodeId: 1,
            10);
        await fixture.Pipeline.StartAsync(initial);
        fixture.MoveTreeToLegacyHistoryAndRestart();

        var restored = await fixture.Pipeline.LoadAsync(
            fixture.SessionService.CurrentSession!.SessionId);

        Assert.IsTrue(restored.IsSuccess);
        Assert.AreEqual(1, restored.Value.TreeNodes.Count);
        Assert.IsTrue(restored.Value.TreeNodes[0].IsActive);
    }

    private static void AssertPending(
        Result<PendingFilterResult> result,
        long beforeCount,
        long afterCount,
        long activeRound)
    {
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(beforeCount, result.Value.BeforeCount);
        Assert.AreEqual(afterCount, result.Value.AfterCount);
        Assert.AreEqual(
            activeRound,
            result.Value.Parent.RoundNumber);
        Assert.AreEqual(
            activeRound,
            result.Value.Round.ParentRoundNumber);
    }

    private static ScanRequest CreateFilter(
        ScanComparisonMode mode)
    {
        return ScanRequest.Create(
            ScanValueType.Int32,
            mode,
            searchValue: null,
            ScanAlignmentMode.Aligned).Value;
    }

    private sealed class PipelineFixture : IDisposable
    {
        private readonly string _root;
        private readonly AppPathService _paths;

        public PipelineFixture()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "MemoryInspector.Tests",
                Guid.NewGuid().ToString("N"));
            _paths = new AppPathService(
                Path.Combine(_root, "ApplicationData"));
            SessionService = new StubSessionService
            {
                CurrentSession = CreateSession(),
            };
            Reader = new MutableMemoryReaderService();
            Storage = new BinarySnapshotStorage(
                _paths,
                TimeProvider.System);
            var matcher = new DefaultValueMatcher();
            var nextScan = new NextScanService(
                SessionService,
                Reader,
                Storage,
                matcher,
                TimeProvider.System);
            var durationFilter = new DurationFilterService(
                SessionService,
                Reader,
                Storage,
                matcher,
                TimeProvider.System);
            NextScan = nextScan;
            DurationFilter = durationFilter;
            HistoryStore = new JsonScanHistoryStore(_paths);
            Pipeline = CreatePipeline();
        }

        public StubSessionService SessionService { get; }

        public MutableMemoryReaderService Reader { get; }

        public BinarySnapshotStorage Storage { get; }

        public INextScanService NextScan { get; }

        public IDurationFilterService DurationFilter { get; }

        public IScanHistoryStore HistoryStore { get; private set; }

        public IFilterPipelineService Pipeline { get; private set; }

        public void RestartPipeline()
        {
            (HistoryStore as IDisposable)?.Dispose();
            HistoryStore = new JsonScanHistoryStore(_paths);
            Pipeline = CreatePipeline();
        }

        public void CorruptHistoryAndRestart()
        {
            (HistoryStore as IDisposable)?.Dispose();
            var historyPath = Path.Combine(
                _paths.TempDirectory,
                SessionService.CurrentSession!.SessionId
                    .ToString("D"),
                "tree.json");
            File.WriteAllText(
                historyPath,
                "{ this is not valid json");
            HistoryStore = new JsonScanHistoryStore(_paths);
            Pipeline = CreatePipeline();
        }

        public void MoveTreeToLegacyHistoryAndRestart()
        {
            (HistoryStore as IDisposable)?.Dispose();
            var sessionId = SessionService.CurrentSession!.SessionId
                .ToString("D");
            var treePath = Path.Combine(
                _paths.TempDirectory,
                sessionId,
                "tree.json");
            var legacyDirectory = Path.Combine(
                _paths.SessionsDirectory,
                sessionId);
            Directory.CreateDirectory(legacyDirectory);
            var legacyJson = File.ReadAllText(treePath)
                .Replace(
                    "\"formatVersion\": 3",
                    "\"formatVersion\": 1",
                    StringComparison.Ordinal)
                .Replace(
                    "      \"isPinned\": false,\r\n",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "      \"isPinned\": false,\n",
                    string.Empty,
                    StringComparison.Ordinal);
            File.WriteAllText(
                Path.Combine(
                    legacyDirectory,
                    "scan-history.json"),
                legacyJson);
            File.Delete(treePath);
            HistoryStore = new JsonScanHistoryStore(_paths);
            Pipeline = CreatePipeline();
        }

        public int SnapshotFileCount()
        {
            var directory = Path.Combine(
                _paths.TempDirectory,
                SessionService.CurrentSession!.SessionId
                    .ToString("D"));
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(
                    directory,
                    "node_*.full.bin",
                    SearchOption.TopDirectoryOnly).Count()
                : 0;
        }

        public async Task<SnapshotDescriptor> WriteSnapshotAsync(
            int nodeId,
            params int[] values)
        {
            var result = await Storage.WriteAsync(
                new SnapshotWriteRequest(
                    SessionService.CurrentSession!.SessionId,
                    nodeId,
                    ScanValueType.Int32,
                    includeValues: true,
                    expectedRecordCount: values.Length),
                Records(values));
            return result.Value;
        }

        public void Dispose()
        {
            (HistoryStore as IDisposable)?.Dispose();
            Storage.Dispose();

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private IFilterPipelineService CreatePipeline()
        {
            return new FilterPipelineService(
                SessionService,
                NextScan,
                DurationFilter,
                Storage,
                HistoryStore);
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

    private sealed class MutableMemoryReaderService
        : IMemoryReaderService
    {
        private readonly Dictionary<ulong, int> _values = [];

        public Error? Failure { get; set; }

        public int BatchCallCount { get; private set; }

        public void SetValues(params int[] values)
        {
            _values.Clear();

            for (var index = 0; index < values.Length; index++)
            {
                _values[BaseAddress + (ulong)index] =
                    values[index];
            }
        }

        public Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new AssertFailedException(
                "The filter pipeline must use batch reads.");
        }

        public Task<Result<T>> TryReadAsync<T>(
            ulong address,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            throw new AssertFailedException(
                "The filter pipeline must use batch reads.");
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            IEnumerable<MemoryReadRequest> requests,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;

            if (Failure is not null)
            {
                return Task.FromResult(
                    Result<MemoryBatchReadResult>.Failure(
                        Failure));
            }

            var items = requests.Select(request =>
            {
                if (!_values.TryGetValue(
                    request.Address,
                    out var value))
                {
                    return new MemoryBatchReadItem(
                        request,
                        Result<MemoryReadResult>.Failure(
                            new Error(
                                ErrorCode.NotFound,
                                "Address is not configured.")));
                }

                return new MemoryBatchReadItem(
                    request,
                    Result<MemoryReadResult>.Success(
                        new MemoryReadResult(
                            request,
                            BitConverter.GetBytes(value))));
            });
            return Task.FromResult(
                Result<MemoryBatchReadResult>.Success(
                    new MemoryBatchReadResult(items)));
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
}

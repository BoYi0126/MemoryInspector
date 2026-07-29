using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Application.Scanning.Snapshots.Comparison;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;
using MemoryInspector.IntegrationTests.ProcessExplorer;
using MemoryInspector.Wpf.Services;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.IntegrationTests.Scanning;

[TestClass]
public sealed class SnapshotCompareViewModelTests
{
    [TestMethod]
    public async Task NodesComparePageAndExportArePresented()
    {
        var left = SnapshotCompareServiceTests.Snapshot(1, 10, 120);
        var right = SnapshotCompareServiceTests.Snapshot(2, 12, 144);
        var summary = new SnapshotComparisonSummary(
            left,
            right,
            addedCount: 3,
            removedCount: 1,
            changedCount: 2,
            unchangedCount: 7);
        var pipeline = new StubPipeline(
            State(
                Round(left, "Before", 0),
                Round(right, "After", 1)));
        var compare = new StubCompareService(summary);
        var exporter = new StubExportService(summary);
        var dialog = new StubFileDialog("comparison.csv");
        using var viewModel = new SnapshotCompareViewModel(
            pipeline,
            compare,
            exporter,
            dialog,
            new TestLogger());

        await viewModel.InitializeAsync();
        await viewModel.ComparePageAsync(1);

        Assert.AreEqual(2, viewModel.Nodes.Count);
        Assert.AreEqual("3", viewModel.AddedDisplay);
        Assert.AreEqual("1", viewModel.RemovedDisplay);
        Assert.AreEqual("2", viewModel.ChangedDisplay);
        Assert.AreEqual("7", viewModel.UnchangedDisplay);
        Assert.AreEqual("+2", viewModel.CountDifferenceDisplay);
        Assert.AreEqual("+24 B",
            viewModel.StorageDifferenceDisplay);
        Assert.AreEqual(1, viewModel.Rows.Count);
        Assert.AreEqual(
            SnapshotDifferenceKind.Added,
            viewModel.Rows[0].Kind);
        Assert.AreEqual("Page 1 of 2", viewModel.PageDisplay);

        await viewModel.ComparePageAsync(2);

        Assert.AreEqual(2L, viewModel.PageNumber);
        Assert.AreEqual(
            SnapshotDifferenceKind.Changed,
            viewModel.Rows[0].Kind);

        await viewModel.ExportAsync();

        Assert.AreEqual(1, exporter.CallCount);
        Assert.AreEqual(
            "comparison.csv",
            exporter.Path);
        StringAssert.Contains(
            viewModel.StatusMessage,
            "Exported");
    }

    [TestMethod]
    public async Task SameNodeSelectionCannotCompare()
    {
        var snapshot =
            SnapshotCompareServiceTests.Snapshot(1, 1);
        var round = Round(snapshot, "Only", 0);
        using var viewModel = new SnapshotCompareViewModel(
            new StubPipeline(State(round)),
            new StubCompareService(
                new SnapshotComparisonSummary(
                    snapshot,
                    snapshot,
                    0,
                    0,
                    0,
                    1)),
            new StubExportService(null),
            new StubFileDialog(null),
            new TestLogger());
        await viewModel.InitializeAsync();

        Assert.IsFalse(viewModel.CanCompare);
        Assert.IsFalse(
            viewModel.CompareCommand.CanExecute(null));
    }

    private static FilterPipelineRound Round(
        SnapshotDescriptor snapshot,
        string name,
        long number)
    {
        // Two roots are sufficient for presentation-selection tests.
        return new FilterPipelineRound(
            Guid.NewGuid(),
            parentRoundId: null,
            roundNumber: 0,
            parentRoundNumber: null,
            name: $"{name} {number}",
            snapshot,
            summary: null,
            input: null,
            createdAt: DateTimeOffset.UtcNow);
    }

    private static FilterPipelineState State(
        params FilterPipelineRound[] rounds)
    {
        return new FilterPipelineState(
            rounds[0],
            pendingResult: null,
            isFiltering: false,
            rounds);
    }

    private sealed class StubCompareService(
        SnapshotComparisonSummary summary) :
        ISnapshotCompareService
    {
        public Task<Result<SnapshotComparisonPage>> CompareAsync(
            SnapshotDescriptor left,
            SnapshotDescriptor right,
            long pageNumber = 1,
            int pageSize =
                SnapshotCompareService.DefaultDifferencePageSize,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var kind = pageNumber == 1
                ? SnapshotDifferenceKind.Added
                : SnapshotDifferenceKind.Changed;
            var item = kind == SnapshotDifferenceKind.Added
                ? new SnapshotDifference(
                    0x1000,
                    kind,
                    null,
                    new byte[] { 1, 0, 0, 0 })
                : new SnapshotDifference(
                    0x2000,
                    kind,
                    new byte[] { 1, 0, 0, 0 },
                    new byte[] { 2, 0, 0, 0 });
            return Task.FromResult(
                Result<SnapshotComparisonPage>.Success(
                    new SnapshotComparisonPage(
                        summary,
                        new PagedResult<SnapshotDifference>(
                            [item],
                            pageNumber,
                            pageSize,
                            totalCount: 600))));
        }

        public Task<Result<SnapshotComparisonSummary>> VisitAsync(
            SnapshotDescriptor left,
            SnapshotDescriptor right,
            Func<
                SnapshotDifference,
                CancellationToken,
                ValueTask> visitor,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubExportService(
        SnapshotComparisonSummary? summary) :
        ISnapshotComparisonExportService
    {
        public int CallCount { get; private set; }

        public string? Path { get; private set; }

        public Task<Result<SnapshotComparisonSummary>> ExportCsvAsync(
            string path,
            SnapshotDescriptor left,
            SnapshotDescriptor right,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Path = path;
            return Task.FromResult(
                summary is null
                    ? Result<SnapshotComparisonSummary>.Failure(
                        new Error(
                            ErrorCode.Unexpected,
                            "No summary."))
                    : Result<SnapshotComparisonSummary>.Success(
                        summary));
        }
    }

    private sealed class StubFileDialog(string? path) :
        ISnapshotCompareFileDialogService
    {
        public string? SelectComparisonExportFile(
            string suggestedFileName)
        {
            return path;
        }
    }

    private sealed class StubPipeline(FilterPipelineState state) :
        IFilterPipelineService
    {
        public FilterPipelineState? CurrentState { get; } = state;

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
                DurationFilterExecutionControl?
                    executionControl = null,
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

        public Task<Result<FilterPipelineState>> SetNodePinnedAsync(
            Guid nodeId,
            bool isPinned,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<FilterPipelineState>> DeleteBranchAsync(
            Guid nodeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Result CloseSession(Guid sessionId) =>
            throw new NotSupportedException();

        public Result<ScanTreeNodeComparison> CompareNodes(
            Guid leftNodeId,
            Guid rightNodeId) =>
            throw new NotSupportedException();

        public Result<IReadOnlyList<ScanTreeNode>> GetChildNodes(
            Guid nodeId) =>
            throw new NotSupportedException();

        public Result<IReadOnlyList<ScanTreeNode>> GetPathToRoot(
            Guid nodeId) =>
            throw new NotSupportedException();
    }
}

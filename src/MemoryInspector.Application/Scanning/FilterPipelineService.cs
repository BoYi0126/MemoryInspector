using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning.History;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed class FilterPipelineService(
    IMonitoringSessionService monitoringSessionService,
    INextScanService nextScanService,
    IDurationFilterService durationFilterService,
    ISnapshotStorage snapshotStorage,
    IScanHistoryStore historyStore) : IFilterPipelineService
{
    private readonly object _sync = new();
    private readonly IMonitoringSessionService _monitoringSessionService =
        Guard.NotNull(monitoringSessionService);
    private readonly INextScanService _nextScanService =
        Guard.NotNull(nextScanService);
    private readonly IDurationFilterService _durationFilterService =
        Guard.NotNull(durationFilterService);
    private readonly ISnapshotStorage _snapshotStorage =
        Guard.NotNull(snapshotStorage);
    private readonly IScanHistoryStore _historyStore =
        Guard.NotNull(historyStore);
    private readonly List<FilterPipelineRound> _rounds = [];
    private FilterPipelineRound? _activeRound;
    private PendingFilterResult? _pendingResult;
    private bool _isFiltering;
    private long _nextRoundNumber = 1;
    private int _nextNodeId = 1;

    public FilterPipelineState? CurrentState
    {
        get
        {
            lock (_sync)
            {
                return CreateState();
            }
        }
    }

    public async Task<Result<FilterPipelineState>> StartAsync(
        SnapshotDescriptor initialSnapshot,
        CancellationToken cancellationToken = default)
    {
        if (initialSnapshot is null)
        {
            return Validation<FilterPipelineState>(
                "An initial snapshot is required.");
        }

        var beginResult = BeginOperation(
            requireActiveRound: false,
            requireNoPendingResult: false);

        if (beginResult.IsFailure)
        {
            return Result<FilterPipelineState>.Failure(
                beginResult.Error);
        }

        try
        {
            var sessionValidation = ValidateSession(
                initialSnapshot.SessionId);

            if (sessionValidation.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    sessionValidation.Error);
            }

            var openResult = await _snapshotStorage.OpenAsync(
                    initialSnapshot.SessionId,
                    initialSnapshot.NodeId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (openResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    openResult.Error);
            }

            if (!HasSameSnapshot(
                initialSnapshot,
                openResult.Value) ||
                !openResult.Value.IncludesValues)
            {
                return Result<FilterPipelineState>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "The initial pipeline snapshot changed or " +
                        "does not include candidate values."));
            }

            var initialRound = new FilterPipelineRound(
                Guid.NewGuid(),
                parentRoundId: null,
                roundNumber: 0,
                parentRoundNumber: null,
                name: "Initial",
                openResult.Value,
                summary: null,
                input: null,
                openResult.Value.CreatedAt);
            FilterPipelineRound[] rounds = [initialRound];
            var saveResult = await _historyStore.SaveAsync(
                    CreateDocument(
                        initialRound,
                        pendingResult: null,
                        rounds),
                    cancellationToken)
                .ConfigureAwait(false);

            if (saveResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    saveResult.Error);
            }

            return CompleteState(
                initialRound,
                pendingResult: null,
                rounds,
                nextRoundNumber: 1,
                NextNodeAfter(openResult.Value.NodeId));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled<FilterPipelineState>(exception);
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<Result<FilterPipelineState>> LoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Validation<FilterPipelineState>(
                "Session ID cannot be empty.");
        }

        var beginResult = BeginOperation(
            requireActiveRound: false,
            requireNoPendingResult: false);

        if (beginResult.IsFailure)
        {
            return Result<FilterPipelineState>.Failure(
                beginResult.Error);
        }

        try
        {
            var sessionValidation = ValidateSession(sessionId);

            if (sessionValidation.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    sessionValidation.Error);
            }

            var loadResult = await _historyStore.LoadAsync(
                    sessionId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (loadResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    loadResult.Error);
            }

            var restoreResult = await RestoreRoundsAsync(
                    loadResult.Value,
                    cancellationToken)
                .ConfigureAwait(false);

            if (restoreResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    restoreResult.Error);
            }

            var restored = restoreResult.Value;
            return CompleteState(
                restored.ActiveRound,
                restored.PendingResult,
                restored.Rounds,
                restored.NextRoundNumber,
                restored.NextNodeId);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled<FilterPipelineState>(exception);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            return Result<FilterPipelineState>.Failure(
                new Error(
                    ErrorCode.Serialization,
                    "Scan history metadata is invalid.",
                    exception));
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<Result<PendingFilterResult>> RunNextScanAsync(
        ScanRequest filter,
        int pageSize = NextScanRequest.DefaultPageSize,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (filter is null)
        {
            return Validation<PendingFilterResult>(
                "A Next Scan filter is required.");
        }

        var beginResult = BeginOperation(
            requireActiveRound: true,
            requireNoPendingResult: true);

        if (beginResult.IsFailure)
        {
            return Result<PendingFilterResult>.Failure(
                beginResult.Error);
        }

        try
        {
            var parent = GetRequiredActiveRound();
            var nodeResult = await ReserveNodeIdAsync(
                    parent.Snapshot.SessionId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (nodeResult.IsFailure)
            {
                return Result<PendingFilterResult>.Failure(
                    nodeResult.Error);
            }

            NextScanRequest request;

            try
            {
                request = new NextScanRequest(
                    parent.Snapshot,
                    nodeResult.Value,
                    filter,
                    pageSize);
            }
            catch (ArgumentException exception)
            {
                return Validation<PendingFilterResult>(
                    exception.Message,
                    exception);
            }

            var result = await _nextScanService.ScanAsync(
                    request,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                return Result<PendingFilterResult>.Failure(
                    result.Error);
            }

            var optimizeResult = await _snapshotStorage.OptimizeAsync(
                    parent.Snapshot,
                    result.Value.Snapshot,
                    cancellationToken)
                .ConfigureAwait(false);

            if (optimizeResult.IsFailure)
            {
                _ = await _snapshotStorage.DeleteAsync(
                        result.Value.Snapshot.SessionId,
                        result.Value.Snapshot.NodeId,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Result<PendingFilterResult>.Failure(
                    optimizeResult.Error);
            }

            var summary = new FilterPipelineSummary(
                FilterPipelineOperationKind.NextScan,
                filter.ComparisonMode,
                parent.CandidateCount,
                result.Value.MatchedCount,
                result.Value.StartedAt,
                result.Value.CompletedAt,
                result.Value.IsPartial,
                result.Value.Warnings.Count,
                result.Value.SuppressedWarningCount);
            return await CommitPendingAsync(
                    parent,
                    optimizeResult.Value,
                    summary,
                    FilterPipelineInput.FromRequest(filter),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled<PendingFilterResult>(exception);
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<Result<PendingFilterResult>>
        RunDurationFilterAsync(
            ScanRequest filter,
            TimeSpan duration,
            DurationFilterObservationMode observationMode,
            TimeSpan? sampleInterval = null,
            int pageSize = DurationFilterRequest.DefaultPageSize,
            DurationFilterExecutionControl? executionControl = null,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
    {
        if (filter is null)
        {
            return Validation<PendingFilterResult>(
                "A Duration Filter is required.");
        }

        var beginResult = BeginOperation(
            requireActiveRound: true,
            requireNoPendingResult: true);

        if (beginResult.IsFailure)
        {
            return Result<PendingFilterResult>.Failure(
                beginResult.Error);
        }

        try
        {
            var parent = GetRequiredActiveRound();
            var nodeResult = await ReserveNodeIdAsync(
                    parent.Snapshot.SessionId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (nodeResult.IsFailure)
            {
                return Result<PendingFilterResult>.Failure(
                    nodeResult.Error);
            }

            DurationFilterRequest request;

            try
            {
                request = new DurationFilterRequest(
                    parent.Snapshot,
                    nodeResult.Value,
                    filter,
                    duration,
                    observationMode,
                    sampleInterval,
                    pageSize);
            }
            catch (ArgumentException exception)
            {
                return Validation<PendingFilterResult>(
                    exception.Message,
                    exception);
            }

            var result =
                await _durationFilterService.FilterAsync(
                        request,
                        executionControl,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (result.IsFailure)
            {
                return Result<PendingFilterResult>.Failure(
                    result.Error);
            }

            var optimizeResult = await _snapshotStorage.OptimizeAsync(
                    parent.Snapshot,
                    result.Value.Snapshot,
                    cancellationToken)
                .ConfigureAwait(false);

            if (optimizeResult.IsFailure)
            {
                _ = await _snapshotStorage.DeleteAsync(
                        result.Value.Snapshot.SessionId,
                        result.Value.Snapshot.NodeId,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Result<PendingFilterResult>.Failure(
                    optimizeResult.Error);
            }

            var summary = new FilterPipelineSummary(
                FilterPipelineOperationKind.DurationFilter,
                filter.ComparisonMode,
                parent.CandidateCount,
                result.Value.MatchedCount,
                result.Value.StartedAt,
                result.Value.CompletedAt,
                result.Value.IsPartial,
                result.Value.Warnings.Count,
                result.Value.SuppressedWarningCount,
                duration,
                observationMode);
            return await CommitPendingAsync(
                    parent,
                    optimizeResult.Value,
                    summary,
                    FilterPipelineInput.FromRequest(filter),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled<PendingFilterResult>(exception);
        }
        finally
        {
            EndOperation();
        }
    }

    public Task<Result<FilterPipelineState>> KeepResultAsync(
        CancellationToken cancellationToken = default)
    {
        return PromotePendingAsync(cancellationToken);
    }

    public Task<Result<FilterPipelineState>> RedoAsync(
        CancellationToken cancellationToken = default)
    {
        return PromotePendingAsync(cancellationToken);
    }

    public Task<Result<FilterPipelineState>> DiscardResultAsync(
        CancellationToken cancellationToken = default)
    {
        return DeletePendingRoundAsync(cancellationToken);
    }

    public async Task<Result<FilterPipelineState>> UndoAsync(
        CancellationToken cancellationToken = default)
    {
        var beginResult = BeginOperation(
            requireActiveRound: true,
            requireNoPendingResult: true);

        if (beginResult.IsFailure)
        {
            return Result<FilterPipelineState>.Failure(
                beginResult.Error);
        }

        try
        {
            FilterPipelineRound active;
            FilterPipelineRound parent;
            FilterPipelineRound[] rounds;

            lock (_sync)
            {
                active = _activeRound!;

                if (active.ParentRoundId is null)
                {
                    return InvalidState<FilterPipelineState>(
                        "The initial round cannot be undone.");
                }

                if (_rounds.Any(round =>
                    round.ParentRoundId == active.RoundId))
                {
                    return InvalidState<FilterPipelineState>(
                        "Only a leaf scan tree node can be undone.");
                }

                parent = _rounds.Single(round =>
                    round.RoundId == active.ParentRoundId.Value);
                rounds = _rounds.ToArray();
            }

            var pending = new PendingFilterResult(
                parent,
                active);
            var saveResult = await _historyStore.SaveAsync(
                    CreateDocument(parent, pending, rounds),
                    cancellationToken)
                .ConfigureAwait(false);

            if (saveResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    saveResult.Error);
            }

            return CompleteState(
                parent,
                pending,
                rounds,
                _nextRoundNumber,
                _nextNodeId);
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<Result<FilterPipelineState>> RenameRoundAsync(
        Guid roundId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (roundId == Guid.Empty)
        {
            return Validation<FilterPipelineState>(
                "Round ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > FilterPipelineRound.MaximumNameLength)
        {
            return Validation<FilterPipelineState>(
                $"Round name must contain 1 to " +
                $"{FilterPipelineRound.MaximumNameLength} characters.");
        }

        var beginResult = BeginOperation(
            requireActiveRound: true,
            requireNoPendingResult: false);

        if (beginResult.IsFailure)
        {
            return Result<FilterPipelineState>.Failure(
                beginResult.Error);
        }

        try
        {
            FilterPipelineRound[] rounds;
            FilterPipelineRound active;
            PendingFilterResult? pending;

            lock (_sync)
            {
                var index = _rounds.FindIndex(round =>
                    round.RoundId == roundId);

                if (index < 0)
                {
                    return Result<FilterPipelineState>.Failure(
                        new Error(
                            ErrorCode.NotFound,
                            "The scan round was not found."));
                }

                rounds = _rounds.ToArray();
                rounds[index] = rounds[index].Rename(name);
                active = rounds.Single(round =>
                    round.RoundId == _activeRound!.RoundId);
                pending = _pendingResult is null
                    ? null
                    : new PendingFilterResult(
                        rounds.Single(round =>
                            round.RoundId ==
                            _pendingResult.Parent.RoundId),
                        rounds.Single(round =>
                            round.RoundId ==
                            _pendingResult.Round.RoundId));
            }

            var saveResult = await _historyStore.SaveAsync(
                    CreateDocument(active, pending, rounds),
                    cancellationToken)
                .ConfigureAwait(false);

            if (saveResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    saveResult.Error);
            }

            return CompleteState(
                active,
                pending,
                rounds,
                _nextRoundNumber,
                _nextNodeId);
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<Result<FilterPipelineState>>
        DeletePendingRoundAsync(
            CancellationToken cancellationToken = default)
    {
        var beginResult = BeginOperation(
            requireActiveRound: true,
            requireNoPendingResult: false);

        if (beginResult.IsFailure)
        {
            return Result<FilterPipelineState>.Failure(
                beginResult.Error);
        }

        try
        {
            FilterPipelineRound active;
            PendingFilterResult pending;
            FilterPipelineRound[] rounds;

            lock (_sync)
            {
                active = _activeRound!;
                pending = _pendingResult ??
                    throw new NoPendingRoundException();

                if (pending.Round.IsPinned)
                {
                    return InvalidState<FilterPipelineState>(
                        "Unpin the pending node before deleting it.");
                }

                rounds = _rounds
                    .Where(round =>
                        round.RoundId !=
                        pending.Round.RoundId)
                    .ToArray();
            }

            var saveResult = await _historyStore.SaveAsync(
                    CreateDocument(
                        active,
                        pendingResult: null,
                        rounds),
                    cancellationToken)
                .ConfigureAwait(false);

            if (saveResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    saveResult.Error);
            }

            var stateResult = CompleteState(
                active,
                pendingResult: null,
                rounds,
                _nextRoundNumber,
                _nextNodeId);
            var deleteResult = await _snapshotStorage.DeleteAsync(
                    pending.Round.Snapshot.SessionId,
                    pending.Round.Snapshot.NodeId,
                    CancellationToken.None)
                .ConfigureAwait(false);

            return deleteResult.IsSuccess ||
                   deleteResult.Error.Code == ErrorCode.NotFound
                ? stateResult
                : Result<FilterPipelineState>.Failure(
                    deleteResult.Error);
        }
        catch (NoPendingRoundException)
        {
            return InvalidState<FilterPipelineState>(
                "There is no pending round to delete.");
        }
        finally
        {
            EndOperation();
        }
    }

    public Task<Result<FilterPipelineState>> BranchFromAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        return SetActiveNodeAsync(nodeId, cancellationToken);
    }

    public async Task<Result<FilterPipelineState>>
        SetActiveNodeAsync(
            Guid nodeId,
            CancellationToken cancellationToken = default)
    {
        if (nodeId == Guid.Empty)
        {
            return Validation<FilterPipelineState>(
                "Node ID cannot be empty.");
        }

        var beginResult = BeginOperation(
            requireActiveRound: true,
            requireNoPendingResult: true);

        if (beginResult.IsFailure)
        {
            return Result<FilterPipelineState>.Failure(
                beginResult.Error);
        }

        try
        {
            FilterPipelineRound target;
            FilterPipelineRound[] rounds;

            lock (_sync)
            {
                target = _rounds.FirstOrDefault(round =>
                    round.RoundId == nodeId) ??
                    throw new NodeNotFoundException();
                rounds = _rounds.ToArray();
            }

            var saveResult = await _historyStore.SaveAsync(
                    CreateDocument(
                        target,
                        pendingResult: null,
                        rounds),
                    cancellationToken)
                .ConfigureAwait(false);

            if (saveResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    saveResult.Error);
            }

            return CompleteState(
                target,
                pendingResult: null,
                rounds,
                _nextRoundNumber,
                _nextNodeId);
        }
        catch (NodeNotFoundException)
        {
            return Result<FilterPipelineState>.Failure(
                new Error(
                    ErrorCode.NotFound,
                    "The scan tree node was not found."));
        }
        finally
        {
            EndOperation();
        }
    }

    public Task<Result<FilterPipelineState>> RenameNodeAsync(
        Guid nodeId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return RenameRoundAsync(
            nodeId,
            name,
            cancellationToken);
    }

    public async Task<Result<FilterPipelineState>>
        SetNodePinnedAsync(
            Guid nodeId,
            bool isPinned,
            CancellationToken cancellationToken = default)
    {
        if (nodeId == Guid.Empty)
        {
            return Validation<FilterPipelineState>(
                "Node ID cannot be empty.");
        }

        var beginResult = BeginOperation(
            requireActiveRound: true,
            requireNoPendingResult: false);

        if (beginResult.IsFailure)
        {
            return Result<FilterPipelineState>.Failure(
                beginResult.Error);
        }

        try
        {
            FilterPipelineRound[] rounds;
            FilterPipelineRound active;
            PendingFilterResult? pending;

            lock (_sync)
            {
                var index = _rounds.FindIndex(round =>
                    round.RoundId == nodeId);

                if (index < 0)
                {
                    throw new NodeNotFoundException();
                }

                rounds = _rounds.ToArray();
                rounds[index] = rounds[index]
                    .SetPinned(isPinned);
                (active, pending) = RebindState(rounds);
            }

            var saveResult = await _historyStore.SaveAsync(
                    CreateDocument(active, pending, rounds),
                    cancellationToken)
                .ConfigureAwait(false);

            if (saveResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    saveResult.Error);
            }

            return CompleteState(
                active,
                pending,
                rounds,
                _nextRoundNumber,
                _nextNodeId);
        }
        catch (NodeNotFoundException)
        {
            return Result<FilterPipelineState>.Failure(
                new Error(
                    ErrorCode.NotFound,
                    "The scan tree node was not found."));
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<Result<FilterPipelineState>>
        DeleteBranchAsync(
            Guid nodeId,
            CancellationToken cancellationToken = default)
    {
        if (nodeId == Guid.Empty)
        {
            return Validation<FilterPipelineState>(
                "Node ID cannot be empty.");
        }

        var beginResult = BeginOperation(
            requireActiveRound: true,
            requireNoPendingResult: true);

        if (beginResult.IsFailure)
        {
            return Result<FilterPipelineState>.Failure(
                beginResult.Error);
        }

        try
        {
            FilterPipelineRound active;
            FilterPipelineRound[] deleted;
            FilterPipelineRound[] retained;

            lock (_sync)
            {
                active = _activeRound!;
                var target = _rounds.FirstOrDefault(round =>
                    round.RoundId == nodeId) ??
                    throw new NodeNotFoundException();

                if (target.ParentRoundId is null)
                {
                    return InvalidState<FilterPipelineState>(
                        "The root scan tree node cannot be deleted.");
                }

                var subtreeIds = GetSubtreeIds(
                    nodeId,
                    _rounds);

                if (subtreeIds.Contains(active.RoundId))
                {
                    return InvalidState<FilterPipelineState>(
                        "Set an active node outside the branch " +
                        "before deleting it.");
                }

                deleted = _rounds
                    .Where(round =>
                        subtreeIds.Contains(round.RoundId))
                    .ToArray();

                if (deleted.Any(round => round.IsPinned))
                {
                    return InvalidState<FilterPipelineState>(
                        "A pinned node prevents branch deletion.");
                }

                retained = _rounds
                    .Where(round =>
                        !subtreeIds.Contains(round.RoundId))
                    .ToArray();
            }

            var saveResult = await _historyStore.SaveAsync(
                    CreateDocument(
                        active,
                        pendingResult: null,
                        retained),
                    cancellationToken)
                .ConfigureAwait(false);

            if (saveResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    saveResult.Error);
            }

            var stateResult = CompleteState(
                active,
                pendingResult: null,
                retained,
                _nextRoundNumber,
                _nextNodeId);

            foreach (var round in deleted
                         .OrderByDescending(round =>
                             round.Snapshot.ChainDepth)
                         .ThenByDescending(round =>
                             round.RoundNumber))
            {
                var deleteResult = await _snapshotStorage.DeleteAsync(
                        round.Snapshot.SessionId,
                        round.Snapshot.NodeId,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                if (deleteResult.IsFailure &&
                    deleteResult.Error.Code != ErrorCode.NotFound)
                {
                    return Result<FilterPipelineState>.Failure(
                        deleteResult.Error);
                }
            }

            return stateResult;
        }
        catch (NodeNotFoundException)
        {
            return Result<FilterPipelineState>.Failure(
                new Error(
                    ErrorCode.NotFound,
                    "The scan tree node was not found."));
        }
        finally
        {
            EndOperation();
        }
    }

    public Result CloseSession(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Session ID cannot be empty."));
        }

        lock (_sync)
        {
            if (_isFiltering)
            {
                return Result.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "A scan operation is running. Stop it before " +
                        "closing temporary session data."));
            }

            if (_activeRound is null ||
                _activeRound.Snapshot.SessionId != sessionId)
            {
                return Result.Success();
            }

            _activeRound = null;
            _pendingResult = null;
            _rounds.Clear();
            _nextRoundNumber = 1;
            _nextNodeId = 1;
            return Result.Success();
        }
    }

    public Result<ScanTreeNodeComparison> CompareNodes(
        Guid leftNodeId,
        Guid rightNodeId)
    {
        lock (_sync)
        {
            var state = CreateState();

            if (state is null)
            {
                return InvalidState<ScanTreeNodeComparison>(
                    "The filter pipeline has not been started.");
            }

            var left = state.TreeNodes.FirstOrDefault(node =>
                node.NodeId == leftNodeId);
            var right = state.TreeNodes.FirstOrDefault(node =>
                node.NodeId == rightNodeId);

            if (left is null || right is null)
            {
                return Result<ScanTreeNodeComparison>.Failure(
                    new Error(
                        ErrorCode.NotFound,
                        "One or both scan tree nodes were not found."));
            }

            var leftAncestors = GetAncestorIds(
                left.NodeId,
                _rounds);
            var rightAncestors = GetAncestorIds(
                right.NodeId,
                _rounds);
            var nearestCommonAncestor =
                rightAncestors.FirstOrDefault(
                    leftAncestors.Contains);

            return Result<ScanTreeNodeComparison>.Success(
                new ScanTreeNodeComparison(
                    left,
                    right,
                    nearestCommonAncestor == Guid.Empty
                        ? null
                        : nearestCommonAncestor,
                    left.NodeId != right.NodeId &&
                    rightAncestors.Contains(left.NodeId),
                    left.NodeId != right.NodeId &&
                    leftAncestors.Contains(right.NodeId)));
        }
    }

    public Result<IReadOnlyList<ScanTreeNode>> GetChildNodes(
        Guid nodeId)
    {
        lock (_sync)
        {
            var state = CreateState();

            if (state is null)
            {
                return InvalidState<IReadOnlyList<ScanTreeNode>>(
                    "The filter pipeline has not been started.");
            }

            var parent = state.TreeNodes.FirstOrDefault(node =>
                node.NodeId == nodeId);

            if (parent is null)
            {
                return Result<IReadOnlyList<ScanTreeNode>>.Failure(
                    new Error(
                        ErrorCode.NotFound,
                        "The scan tree node was not found."));
            }

            IReadOnlyList<ScanTreeNode> children =
                parent.ChildNodeIds
                    .Select(childId =>
                        state.TreeNodes.Single(node =>
                            node.NodeId == childId))
                    .ToArray();
            return Result<IReadOnlyList<ScanTreeNode>>.Success(
                children);
        }
    }

    public Result<IReadOnlyList<ScanTreeNode>> GetPathToRoot(
        Guid nodeId)
    {
        lock (_sync)
        {
            var state = CreateState();

            if (state is null)
            {
                return InvalidState<IReadOnlyList<ScanTreeNode>>(
                    "The filter pipeline has not been started.");
            }

            if (state.TreeNodes.All(node =>
                node.NodeId != nodeId))
            {
                return Result<IReadOnlyList<ScanTreeNode>>.Failure(
                    new Error(
                        ErrorCode.NotFound,
                        "The scan tree node was not found."));
            }

            var path = GetAncestorIds(nodeId, _rounds)
                .Select(id =>
                    state.TreeNodes.Single(node =>
                        node.NodeId == id))
                .Reverse()
                .ToArray();
            return Result<IReadOnlyList<ScanTreeNode>>.Success(
                path);
        }
    }

    private async Task<Result<FilterPipelineState>>
        PromotePendingAsync(
            CancellationToken cancellationToken)
    {
        var beginResult = BeginOperation(
            requireActiveRound: true,
            requireNoPendingResult: false);

        if (beginResult.IsFailure)
        {
            return Result<FilterPipelineState>.Failure(
                beginResult.Error);
        }

        try
        {
            PendingFilterResult pending;
            FilterPipelineRound[] rounds;

            lock (_sync)
            {
                pending = _pendingResult ??
                    throw new NoPendingRoundException();
                rounds = _rounds.ToArray();
            }

            var saveResult = await _historyStore.SaveAsync(
                    CreateDocument(
                        pending.Round,
                        pendingResult: null,
                        rounds),
                    cancellationToken)
                .ConfigureAwait(false);

            if (saveResult.IsFailure)
            {
                return Result<FilterPipelineState>.Failure(
                    saveResult.Error);
            }

            return CompleteState(
                pending.Round,
                pendingResult: null,
                rounds,
                _nextRoundNumber,
                _nextNodeId);
        }
        catch (NoPendingRoundException)
        {
            return InvalidState<FilterPipelineState>(
                "There is no pending result to keep or redo.");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task<Result<PendingFilterResult>>
        CommitPendingAsync(
            FilterPipelineRound parent,
            SnapshotDescriptor snapshot,
            FilterPipelineSummary summary,
            FilterPipelineInput input,
            CancellationToken cancellationToken)
    {
        long roundNumber;
        FilterPipelineRound[] existingRounds;

        lock (_sync)
        {
            if (_activeRound != parent ||
                _pendingResult is not null)
            {
                return InvalidState<PendingFilterResult>(
                    "The active pipeline round changed before the " +
                    "filter completed.");
            }

            if (_nextRoundNumber == long.MaxValue)
            {
                return Result<PendingFilterResult>.Failure(
                    new Error(
                        ErrorCode.ResourceExhausted,
                        "The pipeline round number was exhausted."));
            }

            roundNumber = _nextRoundNumber;
            existingRounds = _rounds.ToArray();
        }

        var round = new FilterPipelineRound(
            Guid.NewGuid(),
            parent.RoundId,
            roundNumber,
            parent.RoundNumber,
            $"Round {roundNumber}",
            snapshot,
            summary,
            input,
            summary.CompletedAt);
        var pending = new PendingFilterResult(parent, round);
        var rounds = existingRounds
            .Append(round)
            .ToArray();
        var saveResult = await _historyStore.SaveAsync(
                CreateDocument(parent, pending, rounds),
                cancellationToken)
            .ConfigureAwait(false);

        if (saveResult.IsFailure)
        {
            _ = await _snapshotStorage.DeleteAsync(
                    snapshot.SessionId,
                    snapshot.NodeId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return Result<PendingFilterResult>.Failure(
                saveResult.Error);
        }

        lock (_sync)
        {
            _rounds.Clear();
            _rounds.AddRange(rounds);
            _pendingResult = pending;
            _nextRoundNumber = roundNumber + 1;
        }

        return Result<PendingFilterResult>.Success(pending);
    }

    private Result BeginOperation(
        bool requireActiveRound,
        bool requireNoPendingResult)
    {
        lock (_sync)
        {
            if (_isFiltering)
            {
                return Result.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "Another pipeline operation is already running."));
            }

            if (requireActiveRound && _activeRound is null)
            {
                return Result.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "The filter pipeline has not been started."));
            }

            if (requireNoPendingResult &&
                _pendingResult is not null)
            {
                return Result.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "Keep or delete the pending result before " +
                        "continuing filtering."));
            }

            _isFiltering = true;
            return Result.Success();
        }
    }

    private void EndOperation()
    {
        lock (_sync)
        {
            _isFiltering = false;
        }
    }

    private FilterPipelineRound GetRequiredActiveRound()
    {
        lock (_sync)
        {
            return _activeRound ??
                throw new InvalidOperationException(
                    "The filter pipeline has not been started.");
        }
    }

    private async Task<Result<int>> ReserveNodeIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            int candidate;

            lock (_sync)
            {
                candidate = _nextNodeId;
            }

            var openResult = await _snapshotStorage.OpenAsync(
                    sessionId,
                    candidate,
                    cancellationToken)
                .ConfigureAwait(false);

            if (openResult.IsFailure &&
                openResult.Error.Code == ErrorCode.NotFound)
            {
                lock (_sync)
                {
                    _nextNodeId = NextNodeAfter(candidate);
                }

                return Result<int>.Success(candidate);
            }

            if (openResult.IsFailure)
            {
                return Result<int>.Failure(openResult.Error);
            }

            if (candidate == int.MaxValue)
            {
                return Result<int>.Failure(
                    new Error(
                        ErrorCode.ResourceExhausted,
                        "No snapshot node ID remains available."));
            }

            lock (_sync)
            {
                _nextNodeId = candidate + 1;
            }
        }
    }

    private async Task<Result<RestoredHistory>> RestoreRoundsAsync(
        ScanHistoryDocument document,
        CancellationToken cancellationToken)
    {
        var records = document.Rounds
            .OrderBy(record => record.RoundNumber)
            .ToArray();
        var rounds = new List<FilterPipelineRound>(
            records.Length);
        var byId = new Dictionary<Guid, FilterPipelineRound>();

        foreach (var record in records)
        {
            var openResult = await _snapshotStorage.OpenAsync(
                    document.SessionId,
                    record.SnapshotNodeId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (openResult.IsFailure)
            {
                return Result<RestoredHistory>.Failure(
                    openResult.Error);
            }

            var snapshot = openResult.Value;

            if (snapshot.ValueType != record.SnapshotValueType ||
                snapshot.RecordCount !=
                    record.SnapshotRecordCount ||
                snapshot.StorageKind !=
                    record.SnapshotStorageKind ||
                snapshot.ParentNodeId !=
                    record.SnapshotParentNodeId ||
                snapshot.ChainDepth !=
                    record.SnapshotChainDepth ||
                snapshot.AccumulatedDeltaBytes !=
                    record.SnapshotAccumulatedDeltaBytes ||
                !snapshot.Checksum.Equals(
                    record.SnapshotChecksum,
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(snapshot.FilePath).Equals(
                    Path.GetFullPath(record.StorageReference),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result<RestoredHistory>.Failure(
                    new Error(
                        ErrorCode.Serialization,
                        "A scan history snapshot reference is invalid."));
            }

            FilterPipelineRound? parent = null;

            if (record.ParentRoundId.HasValue &&
                !byId.TryGetValue(
                    record.ParentRoundId.Value,
                    out parent))
            {
                return Result<RestoredHistory>.Failure(
                    new Error(
                        ErrorCode.Serialization,
                        "A scan history parent reference is invalid."));
            }

            FilterPipelineSummary? summary = null;

            if (record.RoundNumber > 0)
            {
                summary = new FilterPipelineSummary(
                    record.OperationKind!.Value,
                    record.ComparisonMode!.Value,
                    record.BeforeCount,
                    record.AfterCount,
                    record.StartedAt!.Value,
                    record.CompletedAt!.Value,
                    record.IsPartial,
                    record.WarningCount,
                    record.SuppressedWarningCount,
                    record.ObservationDurationTicks.HasValue
                        ? TimeSpan.FromTicks(
                            record.ObservationDurationTicks.Value)
                        : null,
                    record.ObservationMode);
            }

            var round = new FilterPipelineRound(
                record.RoundId,
                record.ParentRoundId,
                record.RoundNumber,
                parent?.RoundNumber,
                record.Name,
                snapshot,
                summary,
                record.Input,
                record.CreatedAt,
                record.IsPinned);
            rounds.Add(round);
            byId.Add(round.RoundId, round);
        }

        if (!byId.TryGetValue(
                document.ActiveRoundId,
                out var active))
        {
            return Serialization<RestoredHistory>(
                "The active scan history round is missing.");
        }

        PendingFilterResult? pending = null;

        if (document.PendingRoundId.HasValue)
        {
            if (!byId.TryGetValue(
                    document.PendingRoundId.Value,
                    out var pendingRound) ||
                pendingRound.ParentRoundId != active.RoundId)
            {
                return Serialization<RestoredHistory>(
                    "The pending scan history round is invalid.");
            }

            pending = new PendingFilterResult(
                active,
                pendingRound);
        }

        var rootCount = rounds.Count(round =>
            round.ParentRoundId is null);
        var hasInvalidParentOrder = rounds.Any(round =>
            round.ParentRoundId.HasValue &&
            (!byId.TryGetValue(
                round.ParentRoundId.Value,
                out var parent) ||
             parent.RoundNumber >= round.RoundNumber));
        var pendingHasChildren = pending is not null &&
            rounds.Any(round =>
                round.ParentRoundId ==
                pending.Round.RoundId);

        if (rootCount != 1 ||
            rounds[0].RoundNumber != 0 ||
            hasInvalidParentOrder ||
            pendingHasChildren)
        {
            return Serialization<RestoredHistory>(
                "Scan history is not a valid scan tree.");
        }

        var maximumRound = rounds.Max(round =>
            round.RoundNumber);
        var maximumNode = rounds.Max(round =>
            round.Snapshot.NodeId);
        return Result<RestoredHistory>.Success(
            new RestoredHistory(
                active,
                pending,
                rounds,
                maximumRound == long.MaxValue
                    ? long.MaxValue
                    : maximumRound + 1,
                NextNodeAfter(maximumNode)));
    }

    private Result<FilterPipelineState> CompleteState(
        FilterPipelineRound active,
        PendingFilterResult? pendingResult,
        IEnumerable<FilterPipelineRound> rounds,
        long nextRoundNumber,
        int nextNodeId)
    {
        lock (_sync)
        {
            _activeRound = active;
            _pendingResult = pendingResult;
            _rounds.Clear();
            _rounds.AddRange(rounds);
            _nextRoundNumber = nextRoundNumber;
            _nextNodeId = nextNodeId;
            _isFiltering = false;
            return Result<FilterPipelineState>.Success(
                CreateState()!);
        }
    }

    private (
        FilterPipelineRound Active,
        PendingFilterResult? Pending)
        RebindState(
            IReadOnlyList<FilterPipelineRound> rounds)
    {
        var active = rounds.Single(round =>
            round.RoundId == _activeRound!.RoundId);
        var pending = _pendingResult is null
            ? null
            : new PendingFilterResult(
                rounds.Single(round =>
                    round.RoundId ==
                    _pendingResult.Parent.RoundId),
                rounds.Single(round =>
                    round.RoundId ==
                    _pendingResult.Round.RoundId));
        return (active, pending);
    }

    private FilterPipelineState? CreateState()
    {
        return _activeRound is null
            ? null
            : new FilterPipelineState(
                _activeRound,
                _pendingResult,
                _isFiltering,
                _rounds);
    }

    private ScanHistoryDocument CreateDocument(
        FilterPipelineRound active,
        PendingFilterResult? pendingResult,
        IEnumerable<FilterPipelineRound> rounds)
    {
        return new ScanHistoryDocument(
            ScanHistoryDocument.CurrentFormatVersion,
            active.Snapshot.SessionId,
            active.RoundId,
            pendingResult?.Round.RoundId,
            rounds.Select(ToHistoryRecord).ToArray());
    }

    private static ScanHistoryRoundRecord ToHistoryRecord(
        FilterPipelineRound round)
    {
        var summary = round.Summary;
        return new ScanHistoryRoundRecord(
            round.RoundId,
            round.ParentRoundId,
            round.RoundNumber,
            round.Name,
            round.IsPinned,
            summary?.OperationKind,
            summary?.ComparisonMode,
            round.Input,
            summary?.BeforeCount ?? round.CandidateCount,
            summary?.AfterCount ?? round.CandidateCount,
            round.CreatedAt,
            summary?.StartedAt,
            summary?.CompletedAt,
            summary?.IsPartial ?? false,
            summary?.WarningCount ?? 0,
            summary?.SuppressedWarningCount ?? 0,
            summary?.ObservationDuration?.Ticks,
            summary?.ObservationMode,
            round.Snapshot.NodeId,
            round.Snapshot.ValueType,
            round.Snapshot.RecordCount,
            round.Snapshot.Checksum,
            round.StorageReference,
            round.Snapshot.StorageKind,
            round.Snapshot.ParentNodeId,
            round.Snapshot.ChainDepth,
            round.Snapshot.AccumulatedDeltaBytes);
    }

    private Result ValidateSession(Guid expectedSessionId)
    {
        var session = _monitoringSessionService.CurrentSession;

        return session?.State ==
                MonitoringSessionState.Connected &&
            session.SessionId == expectedSessionId
            ? Result.Success()
            : Result.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "The pipeline snapshot must belong to the " +
                    "connected monitoring session."));
    }

    private static bool HasSameSnapshot(
        SnapshotDescriptor expected,
        SnapshotDescriptor actual)
    {
        return expected.SessionId == actual.SessionId &&
               expected.NodeId == actual.NodeId &&
               expected.ValueType == actual.ValueType &&
               expected.IncludesValues == actual.IncludesValues &&
               expected.RecordCount == actual.RecordCount &&
               expected.Checksum.Equals(
                   actual.Checksum,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static int NextNodeAfter(int nodeId)
    {
        return nodeId == int.MaxValue
            ? int.MaxValue
            : nodeId + 1;
    }

    private static HashSet<Guid> GetSubtreeIds(
        Guid rootNodeId,
        IReadOnlyList<FilterPipelineRound> rounds)
    {
        var result = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(rootNodeId);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (!result.Add(current))
            {
                continue;
            }

            foreach (var child in rounds.Where(round =>
                round.ParentRoundId == current))
            {
                pending.Push(child.RoundId);
            }
        }

        return result;
    }

    private static IReadOnlyList<Guid> GetAncestorIds(
        Guid nodeId,
        IReadOnlyList<FilterPipelineRound> rounds)
    {
        var byId = rounds.ToDictionary(
            round => round.RoundId);
        var result = new List<Guid>();
        var visited = new HashSet<Guid>();
        var current = nodeId;

        while (byId.TryGetValue(current, out var round))
        {
            if (!visited.Add(current))
            {
                break;
            }

            result.Add(current);

            if (!round.ParentRoundId.HasValue)
            {
                break;
            }

            current = round.ParentRoundId.Value;
        }

        return result;
    }

    private static Result<T> Validation<T>(
        string message,
        Exception? exception = null)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Validation,
                message,
                exception));
    }

    private static Result<T> InvalidState<T>(string message)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.InvalidState,
                message));
    }

    private static Result<T> Serialization<T>(string message)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Serialization,
                message));
    }

    private static Result<T> Cancelled<T>(
        Exception? exception = null)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Cancelled,
                "The filter pipeline operation was cancelled.",
                exception));
    }

    private sealed record RestoredHistory(
        FilterPipelineRound ActiveRound,
        PendingFilterResult? PendingResult,
        IReadOnlyList<FilterPipelineRound> Rounds,
        long NextRoundNumber,
        int NextNodeId);

    private sealed class NoPendingRoundException : Exception;

    private sealed class NodeNotFoundException : Exception;
}

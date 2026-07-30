using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed class ScanWorkflowService(
    IMonitoringSessionService sessionService,
    IExactInitialSnapshotService exactInitialService,
    IUnknownInitialScanService unknownInitialService,
    IFilterPipelineService pipelineService,
    ISnapshotNodeIdAllocator nodeIdAllocator,
    ISnapshotStorage snapshotStorage) : IScanWorkflowService
{
    private readonly IMonitoringSessionService _sessionService =
        Guard.NotNull(sessionService);
    private readonly IExactInitialSnapshotService _exactInitialService =
        Guard.NotNull(exactInitialService);
    private readonly IUnknownInitialScanService _unknownInitialService =
        Guard.NotNull(unknownInitialService);
    private readonly IFilterPipelineService _pipeline =
        Guard.NotNull(pipelineService);
    private readonly ISnapshotNodeIdAllocator _nodeIds =
        Guard.NotNull(nodeIdAllocator);
    private readonly ISnapshotStorage _snapshotStorage =
        Guard.NotNull(snapshotStorage);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FilterPipelineState? CurrentState => _pipeline.CurrentState;

    public Task<Result<UnknownInitialScanEstimate>> EstimateUnknownAsync(
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        CancellationToken cancellationToken = default)
    {
        return _unknownInitialService.EstimateAsync(
            valueType,
            alignmentMode,
            cancellationToken);
    }

    public async Task<Result<ScanWorkflowStartResult>> StartExactAsync(
        ScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var entered = await EnterAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entered.IsFailure)
        {
            return Result<ScanWorkflowStartResult>.Failure(
                entered.Error);
        }

        try
        {
            var session = GetConnectedSession();

            if (session.IsFailure)
            {
                return Result<ScanWorkflowStartResult>.Failure(
                    session.Error);
            }

            var node = await _nodeIds.ReserveAsync(
                    session.Value.SessionId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (node.IsFailure)
            {
                return Result<ScanWorkflowStartResult>.Failure(
                    node.Error);
            }

            ExactInitialScanRequest exactRequest;

            try
            {
                exactRequest = new ExactInitialScanRequest(
                    node.Value,
                    request);
            }
            catch (ArgumentException exception)
            {
                return Validation(exception.Message, exception);
            }

            var scan = await _exactInitialService
                .CreateSnapshotAsync(
                    exactRequest,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            if (scan.IsFailure)
            {
                return Result<ScanWorkflowStartResult>.Failure(
                    scan.Error);
            }

            return await StartPipelineAsync(
                    session.Value,
                    scan.Value.Snapshot,
                    scan.Value.Warnings,
                    scan.Value.IsPartial,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<ScanWorkflowStartResult>> StartUnknownAsync(
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var entered = await EnterAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entered.IsFailure)
        {
            return Result<ScanWorkflowStartResult>.Failure(
                entered.Error);
        }

        try
        {
            var session = GetConnectedSession();

            if (session.IsFailure)
            {
                return Result<ScanWorkflowStartResult>.Failure(
                    session.Error);
            }

            var node = await _nodeIds.ReserveAsync(
                    session.Value.SessionId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (node.IsFailure)
            {
                return Result<ScanWorkflowStartResult>.Failure(
                    node.Error);
            }

            UnknownInitialScanRequest request;

            try
            {
                request = new UnknownInitialScanRequest(
                    node.Value,
                    valueType,
                    alignmentMode);
            }
            catch (ArgumentException exception)
            {
                return Validation(exception.Message, exception);
            }

            var scan = await _unknownInitialService
                .CreateSnapshotAsync(
                    request,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            if (scan.IsFailure)
            {
                return Result<ScanWorkflowStartResult>.Failure(
                    scan.Error);
            }

            return await StartPipelineAsync(
                    session.Value,
                    scan.Value.Snapshot,
                    scan.Value.Warnings,
                    scan.Value.IsPartial,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Result<PendingFilterResult>> RunNextAsync(
        ScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _pipeline.RunNextScanAsync(
            request,
            progress: progress,
            cancellationToken: cancellationToken);
    }

    public Task<Result<FilterPipelineState>> KeepAsync(
        CancellationToken cancellationToken = default) =>
        _pipeline.KeepResultAsync(cancellationToken);

    public Task<Result<FilterPipelineState>> DiscardAsync(
        CancellationToken cancellationToken = default) =>
        _pipeline.DiscardResultAsync(cancellationToken);

    private async Task<Result<ScanWorkflowStartResult>>
        StartPipelineAsync(
            MonitoringSession expectedSession,
            SnapshotDescriptor snapshot,
            IReadOnlyList<Error> warnings,
            bool isPartial,
            CancellationToken cancellationToken)
    {
        var current = _sessionService.CurrentSession;

        if (current?.SessionId != expectedSession.SessionId ||
            current.State != MonitoringSessionState.Connected ||
            current.Identity != expectedSession.Identity)
        {
            await DeleteSnapshotAsync(snapshot).ConfigureAwait(false);
            return Result<ScanWorkflowStartResult>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "The monitoring session changed before the scan was committed."));
        }

        var started = await _pipeline.StartAsync(
                snapshot,
                cancellationToken)
            .ConfigureAwait(false);

        if (started.IsFailure)
        {
            await DeleteSnapshotAsync(snapshot).ConfigureAwait(false);
            return Result<ScanWorkflowStartResult>.Failure(
                started.Error);
        }

        return Result<ScanWorkflowStartResult>.Success(
            new ScanWorkflowStartResult(
                snapshot,
                started.Value,
                warnings,
                isPartial));
    }

    private async Task DeleteSnapshotAsync(
        SnapshotDescriptor snapshot)
    {
        _ = await _snapshotStorage.DeleteAsync(
                snapshot.SessionId,
                snapshot.NodeId,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private Result<MonitoringSession> GetConnectedSession()
    {
        var session = _sessionService.CurrentSession;
        return session?.State == MonitoringSessionState.Connected
            ? Result<MonitoringSession>.Success(session)
            : Result<MonitoringSession>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "A connected monitoring session is required."));
    }

    private async Task<Result> EnterAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException exception)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "The scan workflow was cancelled.",
                    exception));
        }
    }

    private static Result<ScanWorkflowStartResult> Validation(
        string message,
        Exception exception) =>
        Result<ScanWorkflowStartResult>.Failure(
            new Error(
                ErrorCode.Validation,
                message,
                exception));
}

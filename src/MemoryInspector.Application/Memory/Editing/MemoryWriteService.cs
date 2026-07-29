using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.Memory.Editing;

public sealed class MemoryWriteService(
    IMemoryEditorFeatureService featureService,
    IMonitoringSessionService monitoringSessionService,
    IMemoryWriter writer,
    IMemoryWriteAuditService auditService,
    TimeProvider timeProvider) : IMemoryWriteService
{
    private readonly IMemoryEditorFeatureService _featureService =
        Guard.NotNull(featureService);
    private readonly IMonitoringSessionService
        _monitoringSessionService =
            Guard.NotNull(monitoringSessionService);
    private readonly IMemoryWriter _writer =
        Guard.NotNull(writer);
    private readonly IMemoryWriteAuditService _auditService =
        Guard.NotNull(auditService);
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);

    public async Task<MemoryWriteResult> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MemoryWriteResult result;
        var settings = _featureService.State.Settings;

        if (!settings.Enabled)
        {
            result = Failure(
                request,
                MemoryWriteFailureReason.FeatureDisabled,
                ErrorCode.InvalidState,
                "Memory Editor is disabled.");
            return await AuditAsync(request, result)
                .ConfigureAwait(false);
        }

        var session = _monitoringSessionService.CurrentSession;

        if (session?.State != MonitoringSessionState.Connected)
        {
            result = Failure(
                request,
                MemoryWriteFailureReason.TargetExited,
                ErrorCode.InvalidState,
                "The monitoring target is not connected.");
            return await AuditAsync(request, result)
                .ConfigureAwait(false);
        }

        if (session.SessionId != request.SessionId ||
            !IdentitiesMatch(
                session.Identity,
                request.TargetIdentity))
        {
            result = Failure(
                request,
                MemoryWriteFailureReason.SessionInvalid,
                ErrorCode.InvalidState,
                "The active monitoring session identity does not " +
                "match the write request.");
            return await AuditAsync(request, result)
                .ConfigureAwait(false);
        }

        if (request.Source == MemoryWriteSource.ManualAddress &&
            !settings.AllowManualAddress)
        {
            result = Failure(
                request,
                MemoryWriteFailureReason.ManualAddressDisabled,
                ErrorCode.AccessDenied,
                "Manual-address memory editing is disabled.");
            return await AuditAsync(request, result)
                .ConfigureAwait(false);
        }

        if (settings.VerifyAfterWrite &&
            !request.VerifyAfterWrite)
        {
            result = Failure(
                request,
                MemoryWriteFailureReason.InvalidRequest,
                ErrorCode.Validation,
                "Write verification is required by Memory Editor settings.");
            return await AuditAsync(request, result)
                .ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            result = Failure(
                request,
                MemoryWriteFailureReason.Cancelled,
                ErrorCode.Cancelled,
                "The memory write was cancelled.");
            return await AuditAsync(request, result)
                .ConfigureAwait(false);
        }

        try
        {
            result = await _writer.WriteAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            result = MemoryWriteResultFactory.Failed(
                request,
                MemoryWriteFailureReason.Cancelled,
                new Error(
                    ErrorCode.Cancelled,
                    "The memory write was cancelled.",
                    exception),
                _timeProvider.GetUtcNow());
        }
        catch (Exception exception)
        {
            result = MemoryWriteResultFactory.Failed(
                request,
                MemoryWriteFailureReason.Unknown,
                new Error(
                    ErrorCode.Unexpected,
                    "The memory writer failed unexpectedly.",
                    exception),
                _timeProvider.GetUtcNow());
        }

        return await AuditAsync(request, result)
            .ConfigureAwait(false);
    }

    private async Task<MemoryWriteResult> AuditAsync(
        MemoryWriteRequest request,
        MemoryWriteResult result)
    {
        var entry = new MemoryWriteAuditEntry(
            Guid.NewGuid(),
            request.SessionId,
            request.TargetIdentity,
            request.Address,
            request.ValueType,
            result.OriginalValue,
            request.ParsedBytes.Span,
            result.ReadBackValue,
            result.Success,
            result.Verification.Status,
            result.FailureReason,
            result.Error.Code,
            result.Error.Code == ErrorCode.None
                ? null
                : result.Error.ToDisplayMessage(),
            result.CompletedAt,
            request.Source,
            request.UserNote);
        Result audit;

        try
        {
            audit = await _auditService.RecordAsync(
                    entry,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            audit = Result.Failure(
                new Error(
                    ErrorCode.Unexpected,
                    "The memory-write audit service failed.",
                    exception));
        }

        if (audit.IsSuccess)
        {
            return result;
        }

        return new MemoryWriteResult(
            false,
            result.Address,
            result.RequestedByteCount,
            result.WrittenByteCount,
            result.OriginalValue,
            result.RequestedValue.Span,
            result.Verification,
            MemoryWriteFailureReason.AuditFailed,
            new Error(
                ErrorCode.Io,
                "The write attempt could not be recorded in " +
                "the Memory Editor audit log.",
                audit.Error.Exception,
                audit.Error),
            _timeProvider.GetUtcNow());
    }

    private MemoryWriteResult Failure(
        MemoryWriteRequest request,
        MemoryWriteFailureReason reason,
        ErrorCode code,
        string message)
    {
        return MemoryWriteResultFactory.Failed(
            request,
            reason,
            new Error(code, message),
            _timeProvider.GetUtcNow());
    }

    private static bool IdentitiesMatch(
        MonitoringSessionIdentity left,
        MonitoringSessionIdentity right)
    {
        return left.ProcessId == right.ProcessId &&
               left.ProcessStartTime == right.ProcessStartTime &&
               left.Architecture == right.Architecture &&
               string.Equals(
                   left.ProcessName,
                   right.ProcessName,
                   StringComparison.OrdinalIgnoreCase);
    }
}

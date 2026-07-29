using System.ComponentModel;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Windows.Memory.Editing;

public sealed class WindowsMemoryWriter : IMemoryWriter, IDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IMemoryWriterNativeApi _nativeApi;
    private readonly IProcessIdentityValidator _identityValidator;
    private readonly IMonitoringSessionService _monitoringSessionService;
    private readonly MemoryWriteRegionValidator _regionValidator;
    private readonly MemoryWriteVerificationService _verificationService;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public WindowsMemoryWriter(
        IMonitoringSessionService monitoringSessionService,
        TimeProvider timeProvider)
        : this(
            new WindowsMemoryWriterNativeApi(),
            new WindowsProcessIdentityValidator(),
            monitoringSessionService,
            new MemoryWriteRegionValidator(),
            new MemoryWriteVerificationService(),
            timeProvider)
    {
    }

    internal WindowsMemoryWriter(
        IMemoryWriterNativeApi nativeApi,
        IProcessIdentityValidator identityValidator,
        IMonitoringSessionService monitoringSessionService,
        MemoryWriteRegionValidator regionValidator,
        MemoryWriteVerificationService verificationService,
        TimeProvider timeProvider)
    {
        _nativeApi = nativeApi ??
            throw new ArgumentNullException(nameof(nativeApi));
        _identityValidator = identityValidator ??
            throw new ArgumentNullException(nameof(identityValidator));
        _monitoringSessionService = monitoringSessionService ??
            throw new ArgumentNullException(
                nameof(monitoringSessionService));
        _regionValidator = regionValidator ??
            throw new ArgumentNullException(nameof(regionValidator));
        _verificationService = verificationService ??
            throw new ArgumentNullException(nameof(verificationService));
        _timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<MemoryWriteResult> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_disposed)
        {
            return Failure(
                request,
                MemoryWriteFailureReason.Unknown,
                new Error(
                    ErrorCode.InvalidState,
                    "The Windows memory writer has been disposed."));
        }

        var enteredGate = false;

        try
        {
            await _writeGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            enteredGate = true;

            return await Task.Run(
                    () => Write(
                        request,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                request,
                MemoryWriteFailureReason.Cancelled,
                new Error(
                    ErrorCode.Cancelled,
                    "The memory write was cancelled.",
                    exception));
        }
        catch (Win32Exception exception)
        {
            var reason = exception.NativeErrorCode switch
            {
                NativeMemoryConstants.ErrorAccessDenied =>
                    MemoryWriteFailureReason.AccessDenied,
                NativeMemoryConstants.ErrorInvalidHandle or
                NativeMemoryConstants.ErrorInvalidParameter =>
                    MemoryWriteFailureReason.TargetExited,
                _ => MemoryWriteFailureReason.Unknown,
            };
            var code = reason switch
            {
                MemoryWriteFailureReason.AccessDenied =>
                    ErrorCode.AccessDenied,
                MemoryWriteFailureReason.TargetExited =>
                    ErrorCode.NotFound,
                _ => ErrorCode.NativeApi,
            };

            return Failure(
                request,
                reason,
                new Error(
                    code,
                    "The target process could not be opened for writing.",
                    exception));
        }
        catch (Exception exception) when (
            exception is OverflowException or
            NotSupportedException or
            PlatformNotSupportedException)
        {
            return Failure(
                request,
                MemoryWriteFailureReason.Unknown,
                new Error(
                    ErrorCode.NativeApi,
                    "The Windows memory write failed.",
                    exception));
        }
        finally
        {
            if (enteredGate)
            {
                _writeGate.Release();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeGate.Dispose();
    }

    private MemoryWriteResult Write(
        MemoryWriteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessionFailure = ValidateSession(request);

        if (sessionFailure is not null)
        {
            return sessionFailure;
        }

        var identityValidation =
            _identityValidator.Validate(request.TargetIdentity);

        if (identityValidation.IsFailure)
        {
            var reason = identityValidation.Error.Code switch
            {
                ErrorCode.NotFound =>
                    MemoryWriteFailureReason.TargetExited,
                ErrorCode.AccessDenied =>
                    MemoryWriteFailureReason.AccessDenied,
                _ => MemoryWriteFailureReason.SessionInvalid,
            };
            return Failure(
                request,
                reason,
                identityValidation.Error);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var processHandle =
            _nativeApi.OpenProcess(
                request.TargetIdentity.ProcessId);

        if (!_nativeApi.TryQuery(
                processHandle,
                request.Address,
                out var nativeRegion,
                out var queryErrorCode))
        {
            return Failure(
                request,
                MapQueryFailure(queryErrorCode),
                NativeError(
                    queryErrorCode,
                    $"The memory region containing " +
                    $"0x{request.Address:X} could not be located."));
        }

        MemoryRegion region;

        try
        {
            region = MemoryRegionMapper.Map(nativeRegion);
        }
        catch (Exception exception)
            when (exception is ArgumentOutOfRangeException or
                  OverflowException)
        {
            return Failure(
                request,
                MemoryWriteFailureReason.InvalidAddress,
                new Error(
                    ErrorCode.Validation,
                    "Windows returned an invalid memory region.",
                    exception));
        }

        var regionFailure = _regionValidator.Validate(
            region,
            request.Address,
            request.ParsedBytes.Length);

        if (regionFailure != MemoryWriteFailureReason.None)
        {
            return Failure(
                request,
                regionFailure,
                RegionError(regionFailure, request.Address));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var originalValue = new byte[request.ParsedBytes.Length];
        var originalRead = _nativeApi.TryRead(
            processHandle,
            request.Address,
            originalValue,
            out var originalBytesRead,
            out var originalReadErrorCode);

        if (!originalRead ||
            originalBytesRead != originalValue.Length)
        {
            return Failure(
                request,
                originalReadErrorCode ==
                    NativeMemoryConstants.ErrorAccessDenied
                    ? MemoryWriteFailureReason.AccessDenied
                    : MemoryWriteFailureReason.OriginalReadFailed,
                NativeError(
                    originalReadErrorCode,
                    "The original memory value could not be read."));
        }

        if (request.ExpectedOriginalValue is { } expected &&
            !expected.Span.SequenceEqual(originalValue))
        {
            return Failure(
                request,
                MemoryWriteFailureReason.OriginalValueMismatch,
                new Error(
                    ErrorCode.InvalidState,
                    "The original memory value changed before writing."),
                originalValue);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var finalSessionFailure = ValidateSession(request);

        if (finalSessionFailure is not null)
        {
            return finalSessionFailure;
        }

        var requestedValue = request.ParsedBytes.ToArray();
        var writeSucceeded = _nativeApi.TryWrite(
            processHandle,
            request.Address,
            requestedValue,
            out var bytesWritten,
            out var writeErrorCode);
        var safeBytesWritten = Math.Clamp(
            bytesWritten,
            0,
            requestedValue.Length);

        if (!writeSucceeded ||
            bytesWritten != requestedValue.Length)
        {
            var reason = bytesWritten != 0
                ? MemoryWriteFailureReason.PartialWrite
                : writeErrorCode ==
                    NativeMemoryConstants.ErrorAccessDenied
                    ? MemoryWriteFailureReason.AccessDenied
                    : MemoryWriteFailureReason.WriteFailed;
            return Failure(
                request,
                reason,
                NativeError(
                    writeErrorCode,
                    reason == MemoryWriteFailureReason.PartialWrite
                        ? "Only part of the requested value was written."
                        : "The memory value could not be written."),
                originalValue,
                safeBytesWritten);
        }

        if (!request.VerifyAfterWrite)
        {
            return Success(
                request,
                originalValue,
                new MemoryWriteVerificationResult(
                    MemoryWriteVerificationStatus.NotRequested));
        }

        var readBackValue = new byte[requestedValue.Length];
        var readBackSucceeded = _nativeApi.TryRead(
            processHandle,
            request.Address,
            readBackValue,
            out var readBackBytesRead,
            out var readBackErrorCode);

        if (!readBackSucceeded ||
            readBackBytesRead != readBackValue.Length)
        {
            var readError = NativeError(
                readBackErrorCode,
                "The written value could not be read back.");
            var verification = _verificationService.Verify(
                requestedValue,
                readBackValue: null,
                readError);
            return Failure(
                request,
                MemoryWriteFailureReason.VerificationReadFailed,
                readError,
                originalValue,
                requestedValue.Length,
                verification);
        }

        var verified = _verificationService.Verify(
            requestedValue,
            readBackValue);

        if (verified.Status ==
            MemoryWriteVerificationStatus.Mismatch)
        {
            return Failure(
                request,
                MemoryWriteFailureReason.VerificationMismatch,
                verified.Error,
                originalValue,
                requestedValue.Length,
                verified);
        }

        return Success(request, originalValue, verified);
    }

    private MemoryWriteResult? ValidateSession(
        MemoryWriteRequest request)
    {
        var session = _monitoringSessionService.CurrentSession;

        if (session?.State != MonitoringSessionState.Connected)
        {
            return Failure(
                request,
                MemoryWriteFailureReason.TargetExited,
                new Error(
                    ErrorCode.InvalidState,
                    "The monitoring target is not connected."));
        }

        if (session.SessionId != request.SessionId ||
            !IdentitiesMatch(
                session.Identity,
                request.TargetIdentity))
        {
            return Failure(
                request,
                MemoryWriteFailureReason.SessionInvalid,
                new Error(
                    ErrorCode.InvalidState,
                    "The active monitoring session does not " +
                    "match the write request."));
        }

        return null;
    }

    private MemoryWriteResult Success(
        MemoryWriteRequest request,
        ReadOnlyMemory<byte> originalValue,
        MemoryWriteVerificationResult verification)
    {
        return new MemoryWriteResult(
            true,
            request.Address,
            request.ParsedBytes.Length,
            request.ParsedBytes.Length,
            originalValue,
            request.ParsedBytes.Span,
            verification,
            MemoryWriteFailureReason.None,
            Error.None,
            _timeProvider.GetUtcNow());
    }

    private MemoryWriteResult Failure(
        MemoryWriteRequest request,
        MemoryWriteFailureReason reason,
        Error error,
        ReadOnlyMemory<byte>? originalValue = null,
        int writtenByteCount = 0,
        MemoryWriteVerificationResult? verification = null)
    {
        return new MemoryWriteResult(
            false,
            request.Address,
            request.ParsedBytes.Length,
            writtenByteCount,
            originalValue,
            request.ParsedBytes.Span,
            verification ??
                new MemoryWriteVerificationResult(
                    MemoryWriteVerificationStatus.NotRequested),
            reason,
            error,
            _timeProvider.GetUtcNow());
    }

    private static MemoryWriteFailureReason MapQueryFailure(
        int nativeErrorCode)
    {
        return nativeErrorCode switch
        {
            NativeMemoryConstants.ErrorAccessDenied =>
                MemoryWriteFailureReason.AccessDenied,
            NativeMemoryConstants.ErrorInvalidHandle =>
                MemoryWriteFailureReason.TargetExited,
            _ => MemoryWriteFailureReason.RegionNotFound,
        };
    }

    private static Error RegionError(
        MemoryWriteFailureReason reason,
        ulong address)
    {
        var message = reason switch
        {
            MemoryWriteFailureReason.RegionNotCommitted =>
                "The memory region is not committed.",
            MemoryWriteFailureReason.RegionNotWritable =>
                "The memory region is not writable.",
            MemoryWriteFailureReason.GuardPage =>
                "Guard-page memory cannot be written.",
            MemoryWriteFailureReason.RangeOverflow =>
                "The requested memory range overflows the address space.",
            MemoryWriteFailureReason.RegionNotFound =>
                $"No memory region contains 0x{address:X}.",
            _ => "The requested range is outside the memory region.",
        };
        var code = reason is
            MemoryWriteFailureReason.RegionNotWritable or
            MemoryWriteFailureReason.GuardPage
                ? ErrorCode.AccessDenied
                : ErrorCode.Validation;
        return new Error(code, message);
    }

    private static Error NativeError(
        int nativeErrorCode,
        string message)
    {
        var exception = nativeErrorCode == 0
            ? null
            : new Win32Exception(nativeErrorCode);
        var code = nativeErrorCode switch
        {
            NativeMemoryConstants.ErrorAccessDenied =>
                ErrorCode.AccessDenied,
            NativeMemoryConstants.ErrorInvalidAddress or
            NativeMemoryConstants.ErrorNoAccess or
            NativeMemoryConstants.ErrorPartialCopy or
            NativeMemoryConstants.ErrorInvalidParameter =>
                ErrorCode.NotFound,
            _ => ErrorCode.NativeApi,
        };
        return new Error(code, message, exception);
    }

    private static bool IdentitiesMatch(
        MonitoringSessionIdentity left,
        MonitoringSessionIdentity right)
    {
        return left.ProcessId == right.ProcessId &&
               left.ProcessStartTime.ToUniversalTime() ==
                   right.ProcessStartTime.ToUniversalTime() &&
               left.Architecture == right.Architecture &&
               string.Equals(
                   left.ProcessName,
                   right.ProcessName,
                   StringComparison.OrdinalIgnoreCase);
    }
}

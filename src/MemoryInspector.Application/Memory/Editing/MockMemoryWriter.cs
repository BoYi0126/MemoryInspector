using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.Memory.Editing;

public sealed class MockMemoryWriter(
    Guid sessionId,
    MonitoringSessionIdentity targetIdentity,
    TimeProvider timeProvider) : IMemoryWriter
{
    private readonly object _sync = new();
    private readonly Dictionary<ulong, byte[]> _memory = [];
    private readonly Guid _sessionId = sessionId != Guid.Empty
        ? sessionId
        : throw new ArgumentException(
            "Mock writer session ID cannot be empty.",
            nameof(sessionId));
    private readonly MonitoringSessionIdentity _targetIdentity =
        Guard.NotNull(targetIdentity);
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);

    public bool FailVerificationRead { get; set; }

    public byte[]? VerificationReadBackOverride { get; set; }

    public int WriteCallCount { get; private set; }

    public void Seed(ulong address, ReadOnlySpan<byte> value)
    {
        if (value.Length == 0)
        {
            throw new ArgumentException(
                "Mock memory value cannot be empty.",
                nameof(value));
        }

        lock (_sync)
        {
            _memory[address] = value.ToArray();
        }
    }

    public ReadOnlyMemory<byte>? Read(ulong address)
    {
        lock (_sync)
        {
            return _memory.TryGetValue(address, out var value)
                ? new ReadOnlyMemory<byte>(value.ToArray())
                : default(ReadOnlyMemory<byte>?);
        }
    }

    public Task<MemoryWriteResult> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        WriteCallCount++;

        if (request.SessionId != _sessionId ||
            !IdentitiesMatch(
                request.TargetIdentity,
                _targetIdentity))
        {
            return Task.FromResult(
                MemoryWriteResultFactory.Failed(
                    request,
                    MemoryWriteFailureReason.SessionInvalid,
                    new Error(
                        ErrorCode.InvalidState,
                        "The mock writer session identity does not match."),
                    _timeProvider.GetUtcNow()));
        }

        lock (_sync)
        {
            if (!_memory.TryGetValue(
                    request.Address,
                    out var original) ||
                original.Length != request.ParsedBytes.Length)
            {
                return Task.FromResult(
                    MemoryWriteResultFactory.Failed(
                        request,
                        MemoryWriteFailureReason.OriginalReadFailed,
                        new Error(
                            ErrorCode.NotFound,
                            "The mock address has not been seeded."),
                        _timeProvider.GetUtcNow()));
            }

            if (request.ExpectedOriginalValue.HasValue &&
                !request.ExpectedOriginalValue.Value.Span
                    .SequenceEqual(original))
            {
                return Task.FromResult(
                    MemoryWriteResultFactory.Failed(
                        request,
                        MemoryWriteFailureReason.OriginalValueMismatch,
                        new Error(
                            ErrorCode.InvalidState,
                            "The current value no longer matches " +
                            "the expected original value."),
                        _timeProvider.GetUtcNow(),
                        original));
            }

            var originalCopy = original.ToArray();
            _memory[request.Address] =
                request.ParsedBytes.ToArray();

            if (!request.VerifyAfterWrite)
            {
                return Task.FromResult(
                    MemoryWriteResultFactory.Succeeded(
                        request,
                        originalCopy,
                        null,
                        _timeProvider.GetUtcNow()));
            }

            if (FailVerificationRead)
            {
                return Task.FromResult(
                    MemoryWriteResultFactory.Failed(
                        request,
                        MemoryWriteFailureReason.VerificationReadFailed,
                        new Error(
                            ErrorCode.Unexpected,
                            "The mock verification read failed."),
                        _timeProvider.GetUtcNow(),
                        originalCopy,
                        request.ParsedBytes.Length,
                        new MemoryWriteVerificationResult(
                            MemoryWriteVerificationStatus.ReadFailed,
                            error: new Error(
                                ErrorCode.Unexpected,
                                "The mock verification read failed."))));
            }

            var readBack =
                VerificationReadBackOverride?.ToArray() ??
                _memory[request.Address].ToArray();

            if (!readBack.AsSpan().SequenceEqual(
                request.ParsedBytes.Span))
            {
                return Task.FromResult(
                    MemoryWriteResultFactory.Failed(
                        request,
                        MemoryWriteFailureReason.VerificationMismatch,
                        new Error(
                            ErrorCode.InvalidState,
                            "The mock read-back value did not match " +
                            "the requested value."),
                        _timeProvider.GetUtcNow(),
                        originalCopy,
                        request.ParsedBytes.Length,
                        new MemoryWriteVerificationResult(
                            MemoryWriteVerificationStatus.Mismatch,
                            readBack)));
            }

            return Task.FromResult(
                MemoryWriteResultFactory.Succeeded(
                    request,
                    originalCopy,
                    readBack,
                    _timeProvider.GetUtcNow()));
        }
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

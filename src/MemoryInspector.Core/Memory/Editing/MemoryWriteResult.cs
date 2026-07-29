using MemoryInspector.Common;

namespace MemoryInspector.Core.Memory.Editing;

public sealed class MemoryWriteResult
{
    private readonly byte[]? _originalValue;
    private readonly byte[] _requestedValue;

    public MemoryWriteResult(
        bool success,
        ulong address,
        int requestedByteCount,
        int writtenByteCount,
        ReadOnlyMemory<byte>? originalValue,
        ReadOnlySpan<byte> requestedValue,
        MemoryWriteVerificationResult verification,
        MemoryWriteFailureReason failureReason,
        Error? error,
        DateTimeOffset completedAt)
    {
        if (requestedByteCount <= 0 ||
            requestedValue.Length != requestedByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedByteCount));
        }

        if (writtenByteCount < 0 ||
            writtenByteCount > requestedByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(writtenByteCount));
        }

        ArgumentNullException.ThrowIfNull(verification);

        if (success &&
            (failureReason != MemoryWriteFailureReason.None ||
             writtenByteCount != requestedByteCount ||
             verification.Status is
                 MemoryWriteVerificationStatus.Mismatch or
                 MemoryWriteVerificationStatus.ReadFailed))
        {
            throw new ArgumentException(
                "A successful memory write cannot contain a failure.");
        }

        if (!success &&
            failureReason == MemoryWriteFailureReason.None)
        {
            throw new ArgumentException(
                "A failed memory write requires a failure reason.");
        }

        Success = success;
        Address = address;
        RequestedByteCount = requestedByteCount;
        WrittenByteCount = writtenByteCount;
        _originalValue = originalValue?.ToArray();
        _requestedValue = requestedValue.ToArray();
        Verification = verification;
        FailureReason = failureReason;
        Error = error ?? Error.None;
        CompletedAt = completedAt;
    }

    public bool Success { get; }

    public ulong Address { get; }

    public int RequestedByteCount { get; }

    public int WrittenByteCount { get; }

    public ReadOnlyMemory<byte>? OriginalValue =>
        _originalValue is null
            ? default(ReadOnlyMemory<byte>?)
            : new ReadOnlyMemory<byte>(_originalValue);

    public ReadOnlyMemory<byte> RequestedValue =>
        _requestedValue;

    public MemoryWriteVerificationResult Verification { get; }

    public ReadOnlyMemory<byte>? ReadBackValue =>
        Verification.ReadBackValue;

    public MemoryWriteFailureReason FailureReason { get; }

    public Error Error { get; }

    public DateTimeOffset CompletedAt { get; }
}

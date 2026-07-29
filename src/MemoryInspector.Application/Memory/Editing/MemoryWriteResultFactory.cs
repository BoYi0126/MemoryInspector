using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Application.Memory.Editing;

internal static class MemoryWriteResultFactory
{
    public static MemoryWriteResult Succeeded(
        MemoryWriteRequest request,
        ReadOnlyMemory<byte>? originalValue,
        ReadOnlyMemory<byte>? readBackValue,
        DateTimeOffset completedAt)
    {
        var verification = request.VerifyAfterWrite
            ? new MemoryWriteVerificationResult(
                MemoryWriteVerificationStatus.Verified,
                readBackValue ?? request.ParsedBytes)
            : new MemoryWriteVerificationResult(
                MemoryWriteVerificationStatus.NotRequested);
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
            completedAt);
    }

    public static MemoryWriteResult Failed(
        MemoryWriteRequest request,
        MemoryWriteFailureReason reason,
        Error error,
        DateTimeOffset completedAt,
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
            completedAt);
    }
}

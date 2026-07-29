using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Windows.Memory.Editing;

public sealed class MemoryWriteVerificationService
{
    public MemoryWriteVerificationResult Verify(
        ReadOnlySpan<byte> requestedValue,
        ReadOnlyMemory<byte>? readBackValue,
        Error? readError = null)
    {
        if (!readBackValue.HasValue)
        {
            return new MemoryWriteVerificationResult(
                MemoryWriteVerificationStatus.ReadFailed,
                error: readError ??
                    new Error(
                        ErrorCode.NativeApi,
                        "Memory could not be read back after writing."));
        }

        return requestedValue.SequenceEqual(
            readBackValue.Value.Span)
                ? new MemoryWriteVerificationResult(
                    MemoryWriteVerificationStatus.Verified,
                    readBackValue)
                : new MemoryWriteVerificationResult(
                    MemoryWriteVerificationStatus.Mismatch,
                    readBackValue,
                    new Error(
                        ErrorCode.InvalidState,
                        "The read-back value does not match " +
                        "the requested value."));
    }
}

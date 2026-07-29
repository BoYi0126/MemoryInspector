using MemoryInspector.Common;

namespace MemoryInspector.Core.Memory.Editing;

public sealed class MemoryWriteVerificationResult
{
    private readonly byte[]? _readBackValue;

    public MemoryWriteVerificationResult(
        MemoryWriteVerificationStatus status,
        ReadOnlyMemory<byte>? readBackValue = null,
        Error? error = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (status == MemoryWriteVerificationStatus.Verified &&
            !readBackValue.HasValue)
        {
            throw new ArgumentException(
                "Verified writes require a read-back value.",
                nameof(readBackValue));
        }

        Status = status;
        _readBackValue = readBackValue?.ToArray();
        Error = error ?? Error.None;
    }

    public MemoryWriteVerificationStatus Status { get; }

    public ReadOnlyMemory<byte>? ReadBackValue =>
        _readBackValue is null
            ? default(ReadOnlyMemory<byte>?)
            : new ReadOnlyMemory<byte>(_readBackValue);

    public Error Error { get; }
}

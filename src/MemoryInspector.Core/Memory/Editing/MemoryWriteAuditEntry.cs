using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Memory.Editing;

public sealed class MemoryWriteAuditEntry
{
    private readonly byte[]? _originalValue;
    private readonly byte[] _requestedValue;
    private readonly byte[]? _readBackValue;

    public MemoryWriteAuditEntry(
        Guid auditId,
        Guid sessionId,
        MonitoringSessionIdentity targetIdentity,
        ulong address,
        ScanValueType valueType,
        ReadOnlyMemory<byte>? originalValue,
        ReadOnlySpan<byte> requestedValue,
        ReadOnlyMemory<byte>? readBackValue,
        bool success,
        MemoryWriteVerificationStatus verificationStatus,
        MemoryWriteFailureReason failureReason,
        ErrorCode errorCode,
        string? errorMessage,
        DateTimeOffset timestamp,
        MemoryWriteSource source,
        string? userNote)
    {
        if (auditId == Guid.Empty || sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Audit and session IDs cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(targetIdentity);
        var requiredSize = ScanValueTypeInfo.GetSize(valueType);

        if (requestedValue.Length != requiredSize ||
            (originalValue.HasValue &&
             originalValue.Value.Length != requiredSize) ||
            (readBackValue.HasValue &&
             readBackValue.Value.Length != requiredSize))
        {
            throw new ArgumentException(
                "Audit values must match the selected value type.");
        }

        AuditId = auditId;
        SessionId = sessionId;
        TargetIdentity = targetIdentity;
        Address = address;
        ValueType = valueType;
        _originalValue = originalValue?.ToArray();
        _requestedValue = requestedValue.ToArray();
        _readBackValue = readBackValue?.ToArray();
        Success = success;
        VerificationStatus = verificationStatus;
        FailureReason = failureReason;
        ErrorCode = errorCode;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? null
            : errorMessage.Trim();
        Timestamp = timestamp;
        Source = source;
        UserNote = string.IsNullOrWhiteSpace(userNote)
            ? null
            : userNote.Trim();
    }

    public Guid AuditId { get; }

    public Guid SessionId { get; }

    public MonitoringSessionIdentity TargetIdentity { get; }

    public ulong Address { get; }

    public ScanValueType ValueType { get; }

    public ReadOnlyMemory<byte>? OriginalValue =>
        ToNullableMemory(_originalValue);

    public ReadOnlyMemory<byte> RequestedValue => _requestedValue;

    public ReadOnlyMemory<byte>? ReadBackValue =>
        ToNullableMemory(_readBackValue);

    public bool Success { get; }

    public MemoryWriteVerificationStatus VerificationStatus { get; }

    public MemoryWriteFailureReason FailureReason { get; }

    public ErrorCode ErrorCode { get; }

    public string? ErrorMessage { get; }

    public DateTimeOffset Timestamp { get; }

    public MemoryWriteSource Source { get; }

    public string? UserNote { get; }

    private static ReadOnlyMemory<byte>? ToNullableMemory(
        byte[]? value)
    {
        return value is null
            ? default(ReadOnlyMemory<byte>?)
            : new ReadOnlyMemory<byte>(value);
    }
}

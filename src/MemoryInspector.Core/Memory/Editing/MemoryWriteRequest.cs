using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Memory.Editing;

public sealed class MemoryWriteRequest
{
    public const int MaximumUserNoteLength = 1_024;
    private readonly byte[] _parsedBytes;
    private readonly byte[]? _expectedOriginalValue;

    public MemoryWriteRequest(
        Guid sessionId,
        MonitoringSessionIdentity targetIdentity,
        ulong address,
        ScanValueType valueType,
        string inputText,
        ReadOnlySpan<byte> parsedBytes,
        ReadOnlySpan<byte> expectedOriginalValue,
        bool hasExpectedOriginalValue,
        bool verifyAfterWrite,
        MemoryWriteSource source,
        string? userNote,
        DateTimeOffset createdAt)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Session ID cannot be empty.",
                nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(targetIdentity);

        if (string.IsNullOrWhiteSpace(inputText))
        {
            throw new ArgumentException(
                "Memory write input text is required.",
                nameof(inputText));
        }

        var requiredSize = ScanValueTypeInfo.GetSize(valueType);

        if (parsedBytes.Length != requiredSize)
        {
            throw new ArgumentException(
                "Parsed bytes do not match the value type.",
                nameof(parsedBytes));
        }

        if (hasExpectedOriginalValue &&
            expectedOriginalValue.Length != requiredSize)
        {
            throw new ArgumentException(
                "Expected original bytes do not match the value type.",
                nameof(expectedOriginalValue));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        _ = checked(address + (ulong)requiredSize);
        var normalizedNote = string.IsNullOrWhiteSpace(userNote)
            ? null
            : userNote.Trim();

        if (normalizedNote?.Length > MaximumUserNoteLength)
        {
            throw new ArgumentException(
                $"User notes cannot exceed " +
                $"{MaximumUserNoteLength} characters.",
                nameof(userNote));
        }

        SessionId = sessionId;
        TargetIdentity = targetIdentity;
        Address = address;
        ValueType = valueType;
        InputText = inputText.Trim();
        _parsedBytes = parsedBytes.ToArray();
        _expectedOriginalValue = hasExpectedOriginalValue
            ? expectedOriginalValue.ToArray()
            : null;
        VerifyAfterWrite = verifyAfterWrite;
        Source = source;
        UserNote = normalizedNote;
        CreatedAt = createdAt;
    }

    public Guid SessionId { get; }

    public MonitoringSessionIdentity TargetIdentity { get; }

    public ulong Address { get; }

    public ScanValueType ValueType { get; }

    public string InputText { get; }

    public ReadOnlyMemory<byte> ParsedBytes => _parsedBytes;

    public ReadOnlyMemory<byte>? ExpectedOriginalValue =>
        _expectedOriginalValue is null
            ? default(ReadOnlyMemory<byte>?)
            : new ReadOnlyMemory<byte>(_expectedOriginalValue);

    public bool VerifyAfterWrite { get; }

    public MemoryWriteSource Source { get; }

    public string? UserNote { get; }

    public DateTimeOffset CreatedAt { get; }
}

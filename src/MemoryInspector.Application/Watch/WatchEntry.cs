using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Watch;

public sealed class WatchEntry
{
    private readonly byte[]? _previousValue;
    private readonly byte[]? _currentValue;

    public WatchEntry(
        Guid key,
        ulong address,
        ScanValueType valueType,
        ReadOnlyMemory<byte>? previousValue = null,
        ReadOnlyMemory<byte>? currentValue = null,
        DateTimeOffset? lastUpdatedAt = null,
        WatchReadStatus status = WatchReadStatus.Pending,
        string? statusMessage = null)
    {
        if (key == Guid.Empty)
        {
            throw new ArgumentException(
                "Watch key cannot be empty.",
                nameof(key));
        }

        var valueSize = ScanValueTypeInfo.GetSize(valueType);

        if ((previousValue.HasValue &&
             previousValue.Value.Length != valueSize) ||
            (currentValue.HasValue &&
             currentValue.Value.Length != valueSize))
        {
            throw new ArgumentException(
                "Watch values must match the selected value type.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Key = key;
        Address = address;
        ValueType = valueType;
        _previousValue = previousValue?.ToArray();
        _currentValue = currentValue?.ToArray();
        LastUpdatedAt = lastUpdatedAt;
        Status = status;
        StatusMessage = string.IsNullOrWhiteSpace(statusMessage)
            ? null
            : statusMessage.Trim();
    }

    public Guid Key { get; }

    public ulong Address { get; }

    public ScanValueType ValueType { get; }

    public ReadOnlyMemory<byte>? PreviousValue =>
        AsNullableMemory(_previousValue);

    public ReadOnlyMemory<byte>? CurrentValue =>
        AsNullableMemory(_currentValue);

    public DateTimeOffset? LastUpdatedAt { get; }

    public WatchReadStatus Status { get; }

    public string? StatusMessage { get; }

    internal WatchEntry WithSuccessfulRead(
        ReadOnlyMemory<byte> value,
        DateTimeOffset updatedAt)
    {
        return new WatchEntry(
            Key,
            Address,
            ValueType,
            AsNullableMemory(_currentValue),
            value,
            updatedAt,
            WatchReadStatus.Available);
    }

    internal WatchEntry WithFailure(
        WatchReadStatus status,
        string message,
        DateTimeOffset updatedAt)
    {
        return new WatchEntry(
            Key,
            Address,
            ValueType,
            AsNullableMemory(_previousValue),
            AsNullableMemory(_currentValue),
            updatedAt,
            status,
            message);
    }

    internal WatchEntry WithStatus(
        WatchReadStatus status,
        string? message = null)
    {
        return new WatchEntry(
            Key,
            Address,
            ValueType,
            AsNullableMemory(_previousValue),
            AsNullableMemory(_currentValue),
            LastUpdatedAt,
            status,
            message);
    }

    internal WatchEntry ChangeType(
        ScanValueType valueType)
    {
        return new WatchEntry(
            Key,
            Address,
            valueType,
            status: WatchReadStatus.Pending);
    }

    private static ReadOnlyMemory<byte>? AsNullableMemory(
        byte[]? value)
    {
        return value is null
            ? default(ReadOnlyMemory<byte>?)
            : new ReadOnlyMemory<byte>(value);
    }
}

using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning.Results;

public sealed class ResultGridItem
{
    private readonly byte[] _value;

    public ResultGridItem(
        ulong address,
        ScanValueType valueType,
        ReadOnlySpan<byte> value,
        ResultReadStatus readStatus)
    {
        _ = ScanValueTypeInfo.GetSize(valueType);

        if (!Enum.IsDefined(readStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(readStatus));
        }

        var requiredSize = ScanValueTypeInfo.GetSize(valueType);

        if ((readStatus == ResultReadStatus.Available &&
             value.Length != requiredSize) ||
            (readStatus == ResultReadStatus.AddressOnly &&
             value.Length != 0))
        {
            throw new ArgumentException(
                "The result value does not match its read status.",
                nameof(value));
        }

        Address = address;
        ValueType = valueType;
        _value = value.ToArray();
        ReadStatus = readStatus;
    }

    public ulong Address { get; }

    public ScanValueType ValueType { get; }

    public ReadOnlyMemory<byte> Value => _value;

    public ResultReadStatus ReadStatus { get; }
}

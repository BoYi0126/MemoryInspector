using System.Buffers.Binary;
using System.Globalization;
using MemoryInspector.Application.Scanning.Results;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class ResultGridRowViewModel
{
    public ResultGridRowViewModel(ResultGridItem item)
    {
        Item = item ??
            throw new ArgumentNullException(nameof(item));
    }

    public ResultGridItem Item { get; }

    public bool IsStale => false;

    public ulong Address => Item.Address;

    public string AddressDisplay => $"0x{Address:X16}";

    public ScanValueType ValueType => Item.ValueType;

    public string ValueTypeDisplay => ValueType.ToString();

    public ResultReadStatus ReadStatus => Item.ReadStatus;

    public string ReadStatusDisplay => ReadStatus switch
    {
        ResultReadStatus.Available => "Available",
        ResultReadStatus.AddressOnly => "Address only",
        _ => "Unknown",
    };

    public string ValueDisplay =>
        ReadStatus != ResultReadStatus.Available
            ? "—"
            : FormatValue(ValueType, Item.Value.Span);

    internal static int CompareValues(
        ResultGridRowViewModel left,
        ResultGridRowViewModel right)
    {
        var statusComparison =
            left.ReadStatus.CompareTo(right.ReadStatus);

        if (statusComparison != 0 ||
            left.ReadStatus != ResultReadStatus.Available)
        {
            return statusComparison;
        }

        var leftValue = left.Item.Value.Span;
        var rightValue = right.Item.Value.Span;

        return left.ValueType switch
        {
            ScanValueType.Byte =>
                leftValue[0].CompareTo(rightValue[0]),
            ScanValueType.Int16 =>
                BinaryPrimitives.ReadInt16LittleEndian(leftValue)
                    .CompareTo(
                        BinaryPrimitives.ReadInt16LittleEndian(
                            rightValue)),
            ScanValueType.UInt16 =>
                BinaryPrimitives.ReadUInt16LittleEndian(leftValue)
                    .CompareTo(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            rightValue)),
            ScanValueType.Int32 =>
                BinaryPrimitives.ReadInt32LittleEndian(leftValue)
                    .CompareTo(
                        BinaryPrimitives.ReadInt32LittleEndian(
                            rightValue)),
            ScanValueType.UInt32 =>
                BinaryPrimitives.ReadUInt32LittleEndian(leftValue)
                    .CompareTo(
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            rightValue)),
            ScanValueType.Int64 =>
                BinaryPrimitives.ReadInt64LittleEndian(leftValue)
                    .CompareTo(
                        BinaryPrimitives.ReadInt64LittleEndian(
                            rightValue)),
            ScanValueType.UInt64 =>
                BinaryPrimitives.ReadUInt64LittleEndian(leftValue)
                    .CompareTo(
                        BinaryPrimitives.ReadUInt64LittleEndian(
                            rightValue)),
            ScanValueType.Float =>
                BitConverter.ToSingle(leftValue)
                    .CompareTo(BitConverter.ToSingle(rightValue)),
            ScanValueType.Double =>
                BitConverter.ToDouble(leftValue)
                    .CompareTo(BitConverter.ToDouble(rightValue)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(left)),
        };
    }

    internal static string FormatValue(
        ScanValueType valueType,
        ReadOnlySpan<byte> value)
    {
        return valueType switch
        {
            ScanValueType.Byte =>
                value[0].ToString(CultureInfo.InvariantCulture),
            ScanValueType.Int16 =>
                BinaryPrimitives.ReadInt16LittleEndian(value)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.UInt16 =>
                BinaryPrimitives.ReadUInt16LittleEndian(value)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Int32 =>
                BinaryPrimitives.ReadInt32LittleEndian(value)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.UInt32 =>
                BinaryPrimitives.ReadUInt32LittleEndian(value)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Int64 =>
                BinaryPrimitives.ReadInt64LittleEndian(value)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.UInt64 =>
                BinaryPrimitives.ReadUInt64LittleEndian(value)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Float =>
                BitConverter.ToSingle(value)
                    .ToString("R", CultureInfo.InvariantCulture),
            ScanValueType.Double =>
                BitConverter.ToDouble(value)
                    .ToString("R", CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(
                nameof(valueType)),
        };
    }
}

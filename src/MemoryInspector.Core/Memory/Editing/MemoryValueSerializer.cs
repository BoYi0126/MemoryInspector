using System.Buffers.Binary;
using System.Globalization;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Memory.Editing;

public sealed class MemoryValueSerializer(
    IScanValueParser parser,
    MemoryByteOrder byteOrder =
        MemoryByteOrder.LittleEndian) : IMemoryValueSerializer
{
    private readonly IScanValueParser _parser =
        Guard.NotNull(parser);
    private readonly MemoryByteOrder _byteOrder =
        Enum.IsDefined(byteOrder)
            ? byteOrder
            : throw new ArgumentOutOfRangeException(
                nameof(byteOrder));

    public Result<MemoryValueSerialization> Serialize(
        string input,
        ScanValueType valueType,
        MemoryFloatingPointPolicy floatingPointPolicy =
            MemoryFloatingPointPolicy.RejectNonFinite)
    {
        if (!Enum.IsDefined(floatingPointPolicy))
        {
            return Invalid(
                "The floating-point serialization policy is invalid.");
        }

        var parsed = _parser.Parse(input, valueType);

        if (parsed.IsFailure)
        {
            return Result<MemoryValueSerialization>.Failure(
                parsed.Error);
        }

        var littleEndianBytes =
            parsed.Value.Bytes.ToArray();

        if (IsNonFinite(valueType, littleEndianBytes) &&
            floatingPointPolicy ==
                MemoryFloatingPointPolicy.RejectNonFinite)
        {
            return Invalid(
                "NaN and Infinity require the explicit " +
                "AllowExplicitNonFinite policy.");
        }

        var targetBytes = littleEndianBytes.ToArray();

        if (_byteOrder == MemoryByteOrder.BigEndian &&
            targetBytes.Length > 1)
        {
            Array.Reverse(targetBytes);
        }

        var preview = FormatDecimal(
            valueType,
            littleEndianBytes);
        var hex = $"0x{Convert.ToHexString(
            littleEndianBytes.Reverse().ToArray())}";

        return Result<MemoryValueSerialization>.Success(
            new MemoryValueSerialization(
                valueType,
                input,
                targetBytes,
                preview,
                hex,
                _byteOrder));
    }

    private static bool IsNonFinite(
        ScanValueType valueType,
        ReadOnlySpan<byte> bytes)
    {
        return valueType switch
        {
            ScanValueType.Float =>
                !float.IsFinite(BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(bytes))),
            ScanValueType.Double =>
                !double.IsFinite(BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64LittleEndian(bytes))),
            _ => false,
        };
    }

    private static string FormatDecimal(
        ScanValueType valueType,
        ReadOnlySpan<byte> bytes)
    {
        return valueType switch
        {
            ScanValueType.Byte =>
                bytes[0].ToString(CultureInfo.InvariantCulture),
            ScanValueType.Int16 =>
                BinaryPrimitives.ReadInt16LittleEndian(bytes)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.UInt16 =>
                BinaryPrimitives.ReadUInt16LittleEndian(bytes)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Int32 =>
                BinaryPrimitives.ReadInt32LittleEndian(bytes)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.UInt32 =>
                BinaryPrimitives.ReadUInt32LittleEndian(bytes)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Int64 =>
                BinaryPrimitives.ReadInt64LittleEndian(bytes)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.UInt64 =>
                BinaryPrimitives.ReadUInt64LittleEndian(bytes)
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Float =>
                BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(bytes))
                    .ToString("R", CultureInfo.InvariantCulture),
            ScanValueType.Double =>
                BitConverter.Int64BitsToDouble(
                        BinaryPrimitives.ReadInt64LittleEndian(bytes))
                    .ToString("R", CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(
                nameof(valueType)),
        };
    }

    private static Result<MemoryValueSerialization> Invalid(
        string message)
    {
        return Result<MemoryValueSerialization>.Failure(
            new Error(ErrorCode.Validation, message));
    }
}

using System.Buffers.Binary;
using System.Globalization;
using MemoryInspector.Common;

namespace MemoryInspector.Core.Scanning;

public sealed class InvariantScanValueParser : IScanValueParser
{
    public Result<ScanValue> Parse(
        string input,
        ScanValueType valueType)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Invalid(valueType);
        }

        var normalized = input.Trim();

        try
        {
            return valueType switch
            {
                ScanValueType.Byte => ParseByte(normalized),
                ScanValueType.Int16 => ParseInt16(normalized),
                ScanValueType.UInt16 => ParseUInt16(normalized),
                ScanValueType.Int32 => ParseInt32(normalized),
                ScanValueType.UInt32 => ParseUInt32(normalized),
                ScanValueType.Int64 => ParseInt64(normalized),
                ScanValueType.UInt64 => ParseUInt64(normalized),
                ScanValueType.Float => ParseFloat(normalized),
                ScanValueType.Double => ParseDouble(normalized),
                _ => Invalid(valueType),
            };
        }
        catch (Exception exception) when (
            exception is
                ArgumentException or
                OverflowException)
        {
            return Result<ScanValue>.Failure(
                new Error(
                    ErrorCode.Validation,
                    $"'{input}' is not a valid {valueType} value.",
                    exception));
        }
    }

    private static Result<ScanValue> ParseByte(string input)
    {
        return TryParseUnsigned(input, byte.MaxValue, out var value)
            ? Create(ScanValueType.Byte, [(byte)value])
            : Invalid(ScanValueType.Byte);
    }

    private static Result<ScanValue> ParseInt16(string input)
    {
        if (!TryParseSigned(
            input,
            short.MinValue,
            short.MaxValue,
            32_768,
            out var value))
        {
            return Invalid(ScanValueType.Int16);
        }

        var bytes = new byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(
            bytes,
            (short)value);
        return Create(ScanValueType.Int16, bytes);
    }

    private static Result<ScanValue> ParseUInt16(string input)
    {
        if (!TryParseUnsigned(input, ushort.MaxValue, out var value))
        {
            return Invalid(ScanValueType.UInt16);
        }

        var bytes = new byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes,
            (ushort)value);
        return Create(ScanValueType.UInt16, bytes);
    }

    private static Result<ScanValue> ParseInt32(string input)
    {
        if (!TryParseSigned(
            input,
            int.MinValue,
            int.MaxValue,
            2_147_483_648,
            out var value))
        {
            return Invalid(ScanValueType.Int32);
        }

        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes,
            (int)value);
        return Create(ScanValueType.Int32, bytes);
    }

    private static Result<ScanValue> ParseUInt32(string input)
    {
        if (!TryParseUnsigned(input, uint.MaxValue, out var value))
        {
            return Invalid(ScanValueType.UInt32);
        }

        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)value);
        return Create(ScanValueType.UInt32, bytes);
    }

    private static Result<ScanValue> ParseInt64(string input)
    {
        if (!TryParseSigned(
            input,
            long.MinValue,
            long.MaxValue,
            9_223_372_036_854_775_808,
            out var value))
        {
            return Invalid(ScanValueType.Int64);
        }

        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return Create(ScanValueType.Int64, bytes);
    }

    private static Result<ScanValue> ParseUInt64(string input)
    {
        if (!TryParseUnsigned(input, ulong.MaxValue, out var value))
        {
            return Invalid(ScanValueType.UInt64);
        }

        var bytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return Create(ScanValueType.UInt64, bytes);
    }

    private static Result<ScanValue> ParseFloat(string input)
    {
        if (!float.TryParse(
                input,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value) ||
            (float.IsInfinity(value) &&
             !IsExplicitInfinity(input)))
        {
            return Invalid(ScanValueType.Float);
        }

        var bytes = new byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes,
            BitConverter.SingleToInt32Bits(value));
        return Create(ScanValueType.Float, bytes);
    }

    private static Result<ScanValue> ParseDouble(string input)
    {
        if (!double.TryParse(
                input,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value) ||
            (double.IsInfinity(value) &&
             !IsExplicitInfinity(input)))
        {
            return Invalid(ScanValueType.Double);
        }

        var bytes = new byte[sizeof(double)];
        BinaryPrimitives.WriteInt64LittleEndian(
            bytes,
            BitConverter.DoubleToInt64Bits(value));
        return Create(ScanValueType.Double, bytes);
    }

    private static bool TryParseUnsigned(
        string input,
        ulong maximum,
        out ulong value)
    {
        value = 0;

        if (TryGetHex(input, out var negative, out var digits))
        {
            return !negative &&
                   ulong.TryParse(
                       digits,
                       NumberStyles.AllowHexSpecifier,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   value <= maximum;
        }

        return ulong.TryParse(
                   input,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value <= maximum;
    }

    private static bool TryParseSigned(
        string input,
        long minimum,
        long maximum,
        ulong negativeMaximumMagnitude,
        out long value)
    {
        value = 0;

        if (!TryGetHex(input, out var negative, out var digits))
        {
            return long.TryParse(
                       input,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   value >= minimum &&
                   value <= maximum;
        }

        if (!ulong.TryParse(
            digits,
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out var magnitude))
        {
            return false;
        }

        if (!negative)
        {
            if (magnitude > (ulong)maximum)
            {
                return false;
            }

            value = (long)magnitude;
            return true;
        }

        if (magnitude > negativeMaximumMagnitude)
        {
            return false;
        }

        value = magnitude == 9_223_372_036_854_775_808
            ? long.MinValue
            : -(long)magnitude;
        return value >= minimum;
    }

    private static bool TryGetHex(
        string input,
        out bool negative,
        out string digits)
    {
        negative = false;
        var offset = 0;

        if (input.StartsWith("-", StringComparison.Ordinal))
        {
            negative = true;
            offset = 1;
        }
        else if (input.StartsWith("+", StringComparison.Ordinal))
        {
            offset = 1;
        }

        if (input.Length < offset + 3 ||
            input[offset] != '0' ||
            input[offset + 1] is not ('x' or 'X'))
        {
            digits = string.Empty;
            return false;
        }

        digits = input[(offset + 2)..];
        return digits.Length > 0;
    }

    private static bool IsExplicitInfinity(string input)
    {
        return input.Equals(
                   "Infinity",
                   StringComparison.OrdinalIgnoreCase) ||
               input.Equals(
                   "+Infinity",
                   StringComparison.OrdinalIgnoreCase) ||
               input.Equals(
                   "-Infinity",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static Result<ScanValue> Create(
        ScanValueType valueType,
        ReadOnlySpan<byte> bytes)
    {
        return ScanValue.FromBytes(valueType, bytes);
    }

    private static Result<ScanValue> Invalid(
        ScanValueType valueType)
    {
        return Result<ScanValue>.Failure(
            new Error(
                ErrorCode.Validation,
                $"The input is outside the valid {valueType} range " +
                "or format."));
    }
}

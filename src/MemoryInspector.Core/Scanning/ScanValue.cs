using MemoryInspector.Common;

namespace MemoryInspector.Core.Scanning;

public sealed class ScanValue
{
    private readonly byte[] _bytes;

    private ScanValue(
        ScanValueType valueType,
        ReadOnlySpan<byte> bytes)
    {
        ValueType = valueType;
        _bytes = bytes.ToArray();
    }

    public ScanValueType ValueType { get; }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public static Result<ScanValue> FromBytes(
        ScanValueType valueType,
        ReadOnlySpan<byte> bytes)
    {
        int requiredSize;

        try
        {
            requiredSize = ScanValueTypeInfo.GetSize(valueType);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Result<ScanValue>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "The scan value type is invalid.",
                    exception));
        }

        return bytes.Length == requiredSize
            ? Result<ScanValue>.Success(
                new ScanValue(valueType, bytes))
            : Result<ScanValue>.Failure(
                new Error(
                    ErrorCode.Validation,
                    $"A {valueType} value requires " +
                    $"{requiredSize} bytes."));
    }
}

using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Memory.Editing;

public sealed class MemoryValueSerialization
{
    private readonly byte[] _bytes;

    public MemoryValueSerialization(
        ScanValueType valueType,
        string inputText,
        ReadOnlySpan<byte> bytes,
        string decimalPreview,
        string hexadecimalPreview,
        MemoryByteOrder byteOrder)
    {
        if (bytes.Length != ScanValueTypeInfo.GetSize(valueType))
        {
            throw new ArgumentException(
                "Serialized bytes do not match the value type.",
                nameof(bytes));
        }

        ValueType = valueType;
        InputText = string.IsNullOrWhiteSpace(inputText)
            ? throw new ArgumentException(
                "Input text is required.",
                nameof(inputText))
            : inputText.Trim();
        _bytes = bytes.ToArray();
        DecimalPreview = decimalPreview ??
            throw new ArgumentNullException(nameof(decimalPreview));
        HexadecimalPreview = hexadecimalPreview ??
            throw new ArgumentNullException(nameof(hexadecimalPreview));
        ByteOrder = byteOrder;
    }

    public ScanValueType ValueType { get; }

    public string InputText { get; }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public string DecimalPreview { get; }

    public string HexadecimalPreview { get; }

    public string BytePreview =>
        string.Join(
            " ",
            _bytes.Select(value => value.ToString("X2")));

    public MemoryByteOrder ByteOrder { get; }
}

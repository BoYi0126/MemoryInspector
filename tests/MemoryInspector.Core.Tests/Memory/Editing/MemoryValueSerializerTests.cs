using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Tests.Memory.Editing;

[TestClass]
public sealed class MemoryValueSerializerTests
{
    private readonly IMemoryValueSerializer _serializer =
        new MemoryValueSerializer(
            new InvariantScanValueParser(),
            MemoryByteOrder.LittleEndian);

    [TestMethod]
    [DataRow(ScanValueType.Byte, "255", "FF", "255")]
    [DataRow(ScanValueType.Int16, "-32768", "0080", "-32768")]
    [DataRow(ScanValueType.UInt16, "65535", "FFFF", "65535")]
    [DataRow(ScanValueType.Int32, "-1", "FFFFFFFF", "-1")]
    [DataRow(
        ScanValueType.UInt32,
        "4294967295",
        "FFFFFFFF",
        "4294967295")]
    [DataRow(
        ScanValueType.Int64,
        "-9223372036854775808",
        "0000000000000080",
        "-9223372036854775808")]
    [DataRow(
        ScanValueType.UInt64,
        "18446744073709551615",
        "FFFFFFFFFFFFFFFF",
        "18446744073709551615")]
    [DataRow(ScanValueType.Float, "1.5", "0000C03F", "1.5")]
    [DataRow(
        ScanValueType.Double,
        "-2.25",
        "00000000000002C0",
        "-2.25")]
    public void SerializesEverySupportedTypeWithDeterministicBytes(
        ScanValueType valueType,
        string input,
        string expectedBytes,
        string expectedDecimal)
    {
        var result = _serializer.Serialize(input, valueType);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            expectedBytes,
            Convert.ToHexString(result.Value.Bytes.Span));
        Assert.AreEqual(
            expectedDecimal,
            result.Value.DecimalPreview);
        Assert.IsTrue(
            result.Value.HexadecimalPreview.StartsWith(
                "0x",
                StringComparison.Ordinal));
        Assert.AreEqual(
            ScanValueTypeInfo.GetSize(valueType),
            result.Value.Bytes.Length);
    }

    [TestMethod]
    public void BigEndianSerializerReversesTargetBytesButKeepsValuePreview()
    {
        var serializer = new MemoryValueSerializer(
            new InvariantScanValueParser(),
            MemoryByteOrder.BigEndian);

        var result = serializer.Serialize(
            "0x12345678",
            ScanValueType.UInt32);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            "12345678",
            Convert.ToHexString(result.Value.Bytes.Span));
        Assert.AreEqual(
            "305419896",
            result.Value.DecimalPreview);
        Assert.AreEqual(
            "0x12345678",
            result.Value.HexadecimalPreview);
        Assert.AreEqual(
            "12 34 56 78",
            result.Value.BytePreview);
    }

    [TestMethod]
    [DataRow("NaN")]
    [DataRow("+Infinity")]
    [DataRow("-Infinity")]
    public void NonFiniteValuesRequireExplicitPolicy(string input)
    {
        var rejected = _serializer.Serialize(
            input,
            ScanValueType.Double);
        var allowed = _serializer.Serialize(
            input,
            ScanValueType.Double,
            MemoryFloatingPointPolicy.AllowExplicitNonFinite);

        Assert.IsTrue(rejected.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, rejected.Error.Code);
        Assert.IsTrue(allowed.IsSuccess);
        Assert.AreEqual(sizeof(double), allowed.Value.Bytes.Length);
    }

    [TestMethod]
    [DataRow(ScanValueType.Byte, "256")]
    [DataRow(ScanValueType.Int16, "12 trailing")]
    [DataRow(ScanValueType.UInt32, "-1")]
    [DataRow(ScanValueType.Int64, "9223372036854775808")]
    [DataRow(ScanValueType.Float, "1e100")]
    [DataRow(ScanValueType.Double, "1.2.3")]
    public void RejectsOverflowAndIncompleteInput(
        ScanValueType valueType,
        string input)
    {
        var result = _serializer.Serialize(input, valueType);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
    }
}

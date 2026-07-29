using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Tests.Scanning;

[TestClass]
public sealed class InvariantScanValueParserTests
{
    private readonly IScanValueParser _parser =
        new InvariantScanValueParser();

    [TestMethod]
    [DataRow(ScanValueType.Byte, "255", "FF")]
    [DataRow(ScanValueType.Int16, "-32768", "0080")]
    [DataRow(ScanValueType.UInt16, "65535", "FFFF")]
    [DataRow(ScanValueType.Int32, "-2147483648", "00000080")]
    [DataRow(ScanValueType.UInt32, "4294967295", "FFFFFFFF")]
    [DataRow(
        ScanValueType.Int64,
        "-9223372036854775808",
        "0000000000000080")]
    [DataRow(
        ScanValueType.UInt64,
        "18446744073709551615",
        "FFFFFFFFFFFFFFFF")]
    [DataRow(ScanValueType.Float, "1.5", "0000C03F")]
    [DataRow(
        ScanValueType.Double,
        "-2.25",
        "00000000000002C0")]
    public void ParseSupportsEveryScanValueType(
        ScanValueType valueType,
        string input,
        string expectedHex)
    {
        var result = _parser.Parse(input, valueType);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            expectedHex,
            Convert.ToHexString(result.Value.Bytes.Span));
    }

    [TestMethod]
    [DataRow(ScanValueType.Byte, "-1")]
    [DataRow(ScanValueType.Byte, "256")]
    [DataRow(ScanValueType.Int16, "-32769")]
    [DataRow(ScanValueType.Int16, "32768")]
    [DataRow(ScanValueType.UInt16, "-1")]
    [DataRow(ScanValueType.UInt16, "65536")]
    [DataRow(ScanValueType.Int32, "-2147483649")]
    [DataRow(ScanValueType.Int32, "2147483648")]
    [DataRow(ScanValueType.UInt32, "-1")]
    [DataRow(ScanValueType.UInt32, "4294967296")]
    [DataRow(ScanValueType.Int64, "-9223372036854775809")]
    [DataRow(ScanValueType.Int64, "9223372036854775808")]
    [DataRow(ScanValueType.UInt64, "-1")]
    [DataRow(ScanValueType.UInt64, "18446744073709551616")]
    public void ParseRejectsIntegerValuesOutsideTheSelectedRange(
        ScanValueType valueType,
        string input)
    {
        var result = _parser.Parse(input, valueType);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
    }

    [TestMethod]
    [DataRow(ScanValueType.Byte, "0xFF", "FF")]
    [DataRow(ScanValueType.Int16, "-0x8000", "0080")]
    [DataRow(ScanValueType.UInt32, "+0xFFFFFFFF", "FFFFFFFF")]
    [DataRow(
        ScanValueType.Int64,
        "-0x8000000000000000",
        "0000000000000080")]
    public void ParseSupportsExplicitHexadecimalIntegerInput(
        ScanValueType valueType,
        string input,
        string expectedHex)
    {
        var result = _parser.Parse(input, valueType);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            expectedHex,
            Convert.ToHexString(result.Value.Bytes.Span));
    }

    [TestMethod]
    [DataRow(ScanValueType.Float)]
    [DataRow(ScanValueType.Double)]
    public void ParseAcceptsExplicitSpecialFloatingPointValues(
        ScanValueType valueType)
    {
        var nan = _parser.Parse("NaN", valueType);
        var positiveInfinity = _parser.Parse("+Infinity", valueType);
        var negativeInfinity = _parser.Parse("-Infinity", valueType);

        Assert.IsTrue(nan.IsSuccess);
        Assert.IsTrue(positiveInfinity.IsSuccess);
        Assert.IsTrue(negativeInfinity.IsSuccess);
    }

    [TestMethod]
    [DataRow(ScanValueType.Float, "1e100")]
    [DataRow(ScanValueType.Double, "1e5000")]
    [DataRow(ScanValueType.Float, "1,5")]
    [DataRow(ScanValueType.Double, "")]
    public void ParseRejectsOverflowLocaleSpecificAndEmptyInput(
        ScanValueType valueType,
        string input)
    {
        var result = _parser.Parse(input, valueType);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
    }
}

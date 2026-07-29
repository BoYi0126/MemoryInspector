using System.Buffers.Binary;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Tests.Scanning;

[TestClass]
public sealed class DefaultValueMatcherTests
{
    private readonly IValueMatcher _matcher = new DefaultValueMatcher();
    private readonly IScanValueParser _parser =
        new InvariantScanValueParser();

    [TestMethod]
    [DataRow(ScanComparisonMode.ExactValue, 42, 42, true)]
    [DataRow(ScanComparisonMode.ExactValue, 41, 42, false)]
    [DataRow(ScanComparisonMode.Unchanged, 42, 42, true)]
    [DataRow(ScanComparisonMode.Changed, 41, 42, true)]
    [DataRow(ScanComparisonMode.Increased, 43, 42, true)]
    [DataRow(ScanComparisonMode.Decreased, 41, 42, true)]
    [DataRow(ScanComparisonMode.GreaterThan, 43, 42, true)]
    [DataRow(ScanComparisonMode.LessThan, 41, 42, true)]
    public void MatchAppliesIntegerComparisonModes(
        ScanComparisonMode mode,
        int current,
        int comparison,
        bool expected)
    {
        Span<byte> currentBytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(
            currentBytes,
            current);
        var comparisonValue = Parse(
            comparison.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ScanValueType.Int32);

        var result = _matcher.IsMatch(
            currentBytes,
            comparisonValue,
            mode);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(expected, result.Value);
    }

    [TestMethod]
    public void MatchUsesAbsoluteFloatingPointTolerance()
    {
        var comparison = Parse("1.0", ScanValueType.Double);
        var inside = DoubleBytes(1.0009);
        var outside = DoubleBytes(1.0011);

        var insideResult = _matcher.IsMatch(
            inside,
            comparison,
            ScanComparisonMode.ExactValue,
            0.001);
        var outsideResult = _matcher.IsMatch(
            outside,
            comparison,
            ScanComparisonMode.ExactValue,
            0.001);

        Assert.IsTrue(insideResult.Value);
        Assert.IsFalse(outsideResult.Value);
    }

    [TestMethod]
    public void CompiledMatcherCanBeReusedWithoutRepeatedValidation()
    {
        var comparison = Parse("42", ScanValueType.Int32);
        var matcherResult = _matcher.CreateMatcher(
            comparison,
            ScanComparisonMode.ExactValue);
        Span<byte> current = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(current, 42);

        Assert.IsTrue(matcherResult.IsSuccess);
        Assert.IsTrue(matcherResult.Value(current));

        BinaryPrimitives.WriteInt32LittleEndian(current, 41);

        Assert.IsFalse(matcherResult.Value(current));
        Assert.IsFalse(matcherResult.Value(current[..2]));
    }

    [TestMethod]
    public void PairMatcherPreservesSignedAndUnsignedOrdering()
    {
        var signedMatcher = _matcher.CreatePairMatcher(
            ScanValueType.Int32,
            ScanComparisonMode.Decreased).Value;
        var unsignedMatcher = _matcher.CreatePairMatcher(
            ScanValueType.UInt32,
            ScanComparisonMode.Increased).Value;
        Span<byte> signedCurrent = stackalloc byte[sizeof(int)];
        Span<byte> signedPrevious = stackalloc byte[sizeof(int)];
        Span<byte> unsignedCurrent = stackalloc byte[sizeof(uint)];
        Span<byte> unsignedPrevious = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteInt32LittleEndian(
            signedCurrent,
            -2);
        BinaryPrimitives.WriteInt32LittleEndian(
            signedPrevious,
            -1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            unsignedCurrent,
            uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(
            unsignedPrevious,
            0);

        Assert.IsTrue(signedMatcher(
            signedCurrent,
            signedPrevious));
        Assert.IsTrue(unsignedMatcher(
            unsignedCurrent,
            unsignedPrevious));
    }

    [TestMethod]
    public void PairMatcherAppliesToleranceToFloatingChange()
    {
        var unchanged = _matcher.CreatePairMatcher(
            ScanValueType.Double,
            ScanComparisonMode.Unchanged,
            0.001).Value;
        var increased = _matcher.CreatePairMatcher(
            ScanValueType.Double,
            ScanComparisonMode.Increased,
            0.001).Value;

        Assert.IsTrue(unchanged(
            DoubleBytes(1.0005),
            DoubleBytes(1)));
        Assert.IsFalse(increased(
            DoubleBytes(1.0005),
            DoubleBytes(1)));
    }

    [TestMethod]
    public void MatchUsesToleranceForFloatingPointChangeModes()
    {
        var comparison = Parse("1.0", ScanValueType.Double);
        var withinTolerance = DoubleBytes(1.0009);

        var changed = _matcher.IsMatch(
            withinTolerance,
            comparison,
            ScanComparisonMode.Changed,
            0.001);
        var increased = _matcher.IsMatch(
            withinTolerance,
            comparison,
            ScanComparisonMode.Increased,
            0.001);
        var greaterThan = _matcher.IsMatch(
            withinTolerance,
            comparison,
            ScanComparisonMode.GreaterThan,
            0.001);

        Assert.IsFalse(changed.Value);
        Assert.IsFalse(increased.Value);
        Assert.IsTrue(greaterThan.Value);
    }

    [TestMethod]
    public void MatchTreatsNanAsEqualOnlyToNan()
    {
        var nan = Parse("NaN", ScanValueType.Double);
        var nanBytes = DoubleBytes(double.NaN);
        var numberBytes = DoubleBytes(1);

        Assert.IsTrue(_matcher.IsMatch(
            nanBytes,
            nan,
            ScanComparisonMode.ExactValue).Value);
        Assert.IsFalse(_matcher.IsMatch(
            numberBytes,
            nan,
            ScanComparisonMode.ExactValue).Value);
        Assert.IsFalse(_matcher.IsMatch(
            nanBytes,
            Parse("1", ScanValueType.Double),
            ScanComparisonMode.GreaterThan).Value);
    }

    [TestMethod]
    public void MatchRequiresInfinityToHaveTheSameSign()
    {
        var positive = Parse("+Infinity", ScanValueType.Double);

        Assert.IsTrue(_matcher.IsMatch(
            DoubleBytes(double.PositiveInfinity),
            positive,
            ScanComparisonMode.ExactValue).Value);
        Assert.IsFalse(_matcher.IsMatch(
            DoubleBytes(double.NegativeInfinity),
            positive,
            ScanComparisonMode.ExactValue).Value);
    }

    [TestMethod]
    public void MatchRejectsInvalidSizeModeAndTolerance()
    {
        var integer = Parse("42", ScanValueType.Int32);

        var wrongSize = _matcher.IsMatch(
            new byte[sizeof(short)],
            integer,
            ScanComparisonMode.ExactValue);
        var unsupportedMode = _matcher.IsMatch(
            new byte[sizeof(int)],
            integer,
            ScanComparisonMode.UnknownInitialValue);
        var integerTolerance = _matcher.IsMatch(
            new byte[sizeof(int)],
            integer,
            ScanComparisonMode.ExactValue,
            0.1);

        AssertValidationFailure(wrongSize);
        AssertValidationFailure(unsupportedMode);
        AssertValidationFailure(integerTolerance);
    }

    private ScanValue Parse(
        string input,
        ScanValueType valueType)
    {
        return _parser.Parse(input, valueType).Value;
    }

    private static byte[] DoubleBytes(double value)
    {
        var bytes = new byte[sizeof(double)];
        BinaryPrimitives.WriteInt64LittleEndian(
            bytes,
            BitConverter.DoubleToInt64Bits(value));
        return bytes;
    }

    private static void AssertValidationFailure(
        Result<bool> result)
    {
        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
    }
}

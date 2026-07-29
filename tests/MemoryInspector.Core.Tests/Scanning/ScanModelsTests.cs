using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Tests.Scanning;

[TestClass]
public sealed class ScanModelsTests
{
    private readonly IScanValueParser _parser =
        new InvariantScanValueParser();

    [TestMethod]
    [DataRow(ScanValueType.Byte, 1)]
    [DataRow(ScanValueType.Int16, 2)]
    [DataRow(ScanValueType.UInt16, 2)]
    [DataRow(ScanValueType.Int32, 4)]
    [DataRow(ScanValueType.UInt32, 4)]
    [DataRow(ScanValueType.Int64, 8)]
    [DataRow(ScanValueType.UInt64, 8)]
    [DataRow(ScanValueType.Float, 4)]
    [DataRow(ScanValueType.Double, 8)]
    public void ValueTypeInfoReturnsStorageSize(
        ScanValueType valueType,
        int expectedSize)
    {
        Assert.AreEqual(
            expectedSize,
            ScanValueTypeInfo.GetSize(valueType));
    }

    [TestMethod]
    public void ScanValueValidatesSizeAndOwnsItsBytes()
    {
        var source = new byte[] { 1, 2, 3, 4 };
        var result = ScanValue.FromBytes(
            ScanValueType.Int32,
            source);
        var invalid = ScanValue.FromBytes(
            ScanValueType.Int64,
            source);

        source[0] = 9;

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual((byte)1, result.Value.Bytes.Span[0]);
        Assert.IsTrue(invalid.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, invalid.Error.Code);
    }

    [TestMethod]
    public void RequestDerivesAlignedAndUnalignedAddressSteps()
    {
        var value = _parser.Parse(
            "42",
            ScanValueType.Int32).Value;
        var aligned = ScanRequest.Create(
            ScanValueType.Int32,
            ScanComparisonMode.ExactValue,
            value,
            ScanAlignmentMode.Aligned);
        var unaligned = ScanRequest.Create(
            ScanValueType.Int32,
            ScanComparisonMode.ExactValue,
            value,
            ScanAlignmentMode.Unaligned);

        Assert.AreEqual(sizeof(int), aligned.Value.AddressStep);
        Assert.AreEqual(1, unaligned.Value.AddressStep);
        Assert.AreEqual(
            ScanRequest.DefaultMaximumResults,
            aligned.Value.MaximumResults);
    }

    [TestMethod]
    public void RequestUsesTypeSpecificFloatingPointTolerance()
    {
        var floatRequest = CreateUnknown(ScanValueType.Float);
        var doubleRequest = CreateUnknown(ScanValueType.Double);

        Assert.AreEqual(
            ScanRequest.DefaultFloatTolerance,
            floatRequest.FloatingPointTolerance);
        Assert.AreEqual(
            ScanRequest.DefaultDoubleTolerance,
            doubleRequest.FloatingPointTolerance);
    }

    [TestMethod]
    public void InvalidSearchInputCannotCreateAnExactScanRequest()
    {
        var parseResult = _parser.Parse(
            "not-a-number",
            ScanValueType.Int32);
        var requestResult = ScanRequest.Create(
            ScanValueType.Int32,
            ScanComparisonMode.ExactValue,
            searchValue: null,
            ScanAlignmentMode.Aligned);

        Assert.IsTrue(parseResult.IsFailure);
        Assert.IsTrue(requestResult.IsFailure);
        Assert.AreEqual(
            ErrorCode.Validation,
            requestResult.Error.Code);
    }

    [TestMethod]
    public void RequestRejectsMismatchedTypeAndInvalidTolerance()
    {
        var intValue = _parser.Parse(
            "42",
            ScanValueType.Int32).Value;
        var mismatchedType = ScanRequest.Create(
            ScanValueType.Int64,
            ScanComparisonMode.ExactValue,
            intValue,
            ScanAlignmentMode.Aligned);
        var integerTolerance = ScanRequest.Create(
            ScanValueType.Int32,
            ScanComparisonMode.ExactValue,
            intValue,
            ScanAlignmentMode.Aligned,
            floatingPointTolerance: 0.1);
        var invalidMaximum = ScanRequest.Create(
            ScanValueType.Int32,
            ScanComparisonMode.ExactValue,
            intValue,
            ScanAlignmentMode.Aligned,
            maximumResults: 0);

        Assert.IsTrue(mismatchedType.IsFailure);
        Assert.IsTrue(integerTolerance.IsFailure);
        Assert.IsTrue(invalidMaximum.IsFailure);
    }

    [TestMethod]
    public void CandidateAddressPreservesA64BitAddress()
    {
        const ulong address = 0xFFFF_FFFF_0000_1234;

        var candidate = new CandidateAddress(address);

        Assert.AreEqual(address, candidate.Address);
    }

    [TestMethod]
    public void ScanResultExposesValidatedSummaryMetadata()
    {
        var request = CreateUnknown(ScanValueType.Byte);
        var startedAt = new DateTimeOffset(
            2026,
            7,
            29,
            10,
            0,
            0,
            TimeSpan.Zero);
        var result = new ScanResult(
            Guid.NewGuid(),
            request,
            scannedBytes: 4_096,
            candidateCount: 25,
            startedAt,
            startedAt.AddSeconds(2),
            isPartial: true);

        Assert.AreEqual(4_096L, result.ScannedBytes);
        Assert.AreEqual(25L, result.CandidateCount);
        Assert.AreEqual(TimeSpan.FromSeconds(2), result.Duration);
        Assert.IsTrue(result.IsPartial);
    }

    private static ScanRequest CreateUnknown(
        ScanValueType valueType)
    {
        return ScanRequest.Create(
            valueType,
            ScanComparisonMode.UnknownInitialValue,
            searchValue: null,
            ScanAlignmentMode.Aligned).Value;
    }
}

using MemoryInspector.Common;

namespace MemoryInspector.Core.Scanning;

public interface IValueMatcher
{
    Result<ScanValuePairMatcher> CreatePairMatcher(
        ScanValueType valueType,
        ScanComparisonMode comparisonMode,
        double floatingPointTolerance = 0);

    Result<ScanValueMatcher> CreateMatcher(
        ScanValue comparisonValue,
        ScanComparisonMode comparisonMode,
        double floatingPointTolerance = 0);

    Result<bool> IsMatch(
        ReadOnlySpan<byte> currentValue,
        ScanValue comparisonValue,
        ScanComparisonMode comparisonMode,
        double floatingPointTolerance = 0);
}

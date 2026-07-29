using MemoryInspector.Common;

namespace MemoryInspector.Core.Scanning;

public sealed record ScanRequest
{
    public const int DefaultMaximumResults = 1_000_000;
    public const double DefaultFloatTolerance = 1e-5;
    public const double DefaultDoubleTolerance = 1e-9;

    private ScanRequest(
        ScanValueType valueType,
        ScanComparisonMode comparisonMode,
        ScanValue? searchValue,
        ScanAlignmentMode alignmentMode,
        double floatingPointTolerance,
        int maximumResults)
    {
        ValueType = valueType;
        ComparisonMode = comparisonMode;
        SearchValue = searchValue;
        AlignmentMode = alignmentMode;
        FloatingPointTolerance = floatingPointTolerance;
        MaximumResults = maximumResults;
    }

    public ScanValueType ValueType { get; }

    public ScanComparisonMode ComparisonMode { get; }

    public ScanValue? SearchValue { get; }

    public ScanAlignmentMode AlignmentMode { get; }

    public double FloatingPointTolerance { get; }

    public int MaximumResults { get; }

    public int ValueSize => ScanValueTypeInfo.GetSize(ValueType);

    public int AddressStep => AlignmentMode == ScanAlignmentMode.Aligned
        ? ValueSize
        : 1;

    public static Result<ScanRequest> Create(
        ScanValueType valueType,
        ScanComparisonMode comparisonMode,
        ScanValue? searchValue,
        ScanAlignmentMode alignmentMode,
        double? floatingPointTolerance = null,
        int maximumResults = DefaultMaximumResults)
    {
        try
        {
            _ = ScanValueTypeInfo.GetSize(valueType);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Validation(
                "The scan value type is invalid.",
                exception);
        }

        if (!Enum.IsDefined(comparisonMode))
        {
            return Validation("The scan comparison mode is invalid.");
        }

        if (!Enum.IsDefined(alignmentMode))
        {
            return Validation("The scan alignment mode is invalid.");
        }

        if (maximumResults <= 0)
        {
            return Validation(
                "Maximum results must be greater than zero.");
        }

        if (searchValue is not null &&
            searchValue.ValueType != valueType)
        {
            return Validation(
                "The search value type does not match the request.");
        }

        if (RequiresSearchValue(comparisonMode) &&
            searchValue is null)
        {
            return Validation(
                $"{comparisonMode} requires a valid search value.");
        }

        var tolerance = floatingPointTolerance ??
            DefaultTolerance(valueType);

        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            return Validation(
                "Floating-point tolerance must be finite and non-negative.");
        }

        if (!ScanValueTypeInfo.IsFloatingPoint(valueType) &&
            tolerance != 0)
        {
            return Validation(
                "Integer scan types do not use floating-point tolerance.");
        }

        return Result<ScanRequest>.Success(
            new ScanRequest(
                valueType,
                comparisonMode,
                searchValue,
                alignmentMode,
                tolerance,
                maximumResults));
    }

    private static bool RequiresSearchValue(
        ScanComparisonMode mode)
    {
        return mode is
            ScanComparisonMode.ExactValue or
            ScanComparisonMode.GreaterThan or
            ScanComparisonMode.LessThan;
    }

    private static double DefaultTolerance(
        ScanValueType valueType)
    {
        return valueType switch
        {
            ScanValueType.Float => DefaultFloatTolerance,
            ScanValueType.Double => DefaultDoubleTolerance,
            _ => 0,
        };
    }

    private static Result<ScanRequest> Validation(
        string message,
        Exception? exception = null)
    {
        return Result<ScanRequest>.Failure(
            new Error(
                ErrorCode.Validation,
                message,
                exception));
    }
}

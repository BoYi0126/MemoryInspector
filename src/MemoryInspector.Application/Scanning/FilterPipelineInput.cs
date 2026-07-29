using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed record FilterPipelineInput
{
    public FilterPipelineInput(
        ScanValueType valueType,
        ScanComparisonMode comparisonMode,
        string? searchValueHex,
        ScanAlignmentMode alignmentMode,
        double floatingPointTolerance,
        int maximumResults)
    {
        _ = ScanValueTypeInfo.GetSize(valueType);

        if (!Enum.IsDefined(comparisonMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(comparisonMode));
        }

        if (!Enum.IsDefined(alignmentMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignmentMode));
        }

        if (searchValueHex is not null)
        {
            if (searchValueHex.Length !=
                    ScanValueTypeInfo.GetSize(valueType) * 2 ||
                searchValueHex.Any(character =>
                    !Uri.IsHexDigit(character)))
            {
                throw new ArgumentException(
                    "Search value hex is invalid.",
                    nameof(searchValueHex));
            }
        }

        if (!double.IsFinite(floatingPointTolerance) ||
            floatingPointTolerance < 0 ||
            maximumResults <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(floatingPointTolerance));
        }

        ValueType = valueType;
        ComparisonMode = comparisonMode;
        SearchValueHex = searchValueHex;
        AlignmentMode = alignmentMode;
        FloatingPointTolerance = floatingPointTolerance;
        MaximumResults = maximumResults;
    }

    public ScanValueType ValueType { get; }

    public ScanComparisonMode ComparisonMode { get; }

    public string? SearchValueHex { get; }

    public ScanAlignmentMode AlignmentMode { get; }

    public double FloatingPointTolerance { get; }

    public int MaximumResults { get; }

    public static FilterPipelineInput FromRequest(
        ScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new FilterPipelineInput(
            request.ValueType,
            request.ComparisonMode,
            request.SearchValue is null
                ? null
                : Convert.ToHexString(
                    request.SearchValue.Bytes.Span),
            request.AlignmentMode,
            request.FloatingPointTolerance,
            request.MaximumResults);
    }
}

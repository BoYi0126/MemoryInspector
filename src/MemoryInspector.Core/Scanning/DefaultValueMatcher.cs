using System.Buffers.Binary;
using MemoryInspector.Common;

namespace MemoryInspector.Core.Scanning;

public sealed class DefaultValueMatcher : IValueMatcher
{
    public Result<ScanValuePairMatcher> CreatePairMatcher(
        ScanValueType valueType,
        ScanComparisonMode comparisonMode,
        double floatingPointTolerance = 0)
    {
        var validation = ValidateMatcher(
            valueType,
            comparisonMode,
            floatingPointTolerance);

        if (validation.IsFailure)
        {
            return Result<ScanValuePairMatcher>.Failure(
                validation.Error);
        }

        var requiredSize = ScanValueTypeInfo.GetSize(valueType);
        ScanValuePairMatcher matcher =
            (currentValue, comparisonValue) =>
                currentValue.Length == requiredSize &&
                comparisonValue.Length == requiredSize &&
                MatchValidated(
                    currentValue,
                    comparisonValue,
                    valueType,
                    comparisonMode,
                    floatingPointTolerance);

        return Result<ScanValuePairMatcher>.Success(matcher);
    }

    public Result<ScanValueMatcher> CreateMatcher(
        ScanValue comparisonValue,
        ScanComparisonMode comparisonMode,
        double floatingPointTolerance = 0)
    {
        ArgumentNullException.ThrowIfNull(comparisonValue);

        var validation = ValidateMatcher(
            comparisonValue.ValueType,
            comparisonMode,
            floatingPointTolerance);

        if (validation.IsFailure)
        {
            return Result<ScanValueMatcher>.Failure(
                validation.Error);
        }

        var requiredSize = ScanValueTypeInfo.GetSize(
            comparisonValue.ValueType);
        var comparisonBytes = comparisonValue.Bytes.ToArray();
        ScanValueMatcher matcher = currentValue =>
            currentValue.Length == requiredSize &&
            MatchValidated(
                currentValue,
                comparisonBytes,
                comparisonValue.ValueType,
                comparisonMode,
                floatingPointTolerance);

        return Result<ScanValueMatcher>.Success(matcher);
    }

    public Result<bool> IsMatch(
        ReadOnlySpan<byte> currentValue,
        ScanValue comparisonValue,
        ScanComparisonMode comparisonMode,
        double floatingPointTolerance = 0)
    {
        var matcherResult = CreateMatcher(
            comparisonValue,
            comparisonMode,
            floatingPointTolerance);

        if (matcherResult.IsFailure)
        {
            return Result<bool>.Failure(matcherResult.Error);
        }

        var requiredSize = ScanValueTypeInfo.GetSize(
            comparisonValue.ValueType);

        return currentValue.Length == requiredSize
            ? Result<bool>.Success(
                matcherResult.Value(currentValue))
            : Invalid(
                $"A {comparisonValue.ValueType} comparison requires " +
                $"{requiredSize} current-value bytes.");
    }

    private static bool MatchValidated(
        ReadOnlySpan<byte> currentValue,
        ReadOnlySpan<byte> comparisonBytes,
        ScanValueType valueType,
        ScanComparisonMode comparisonMode,
        double floatingPointTolerance)
    {
        return valueType switch
        {
            ScanValueType.Byte => MatchComparable(
                currentValue[0],
                comparisonBytes[0],
                comparisonMode),
            ScanValueType.Int16 => MatchComparable(
                BinaryPrimitives.ReadInt16LittleEndian(currentValue),
                BinaryPrimitives.ReadInt16LittleEndian(comparisonBytes),
                comparisonMode),
            ScanValueType.UInt16 => MatchComparable(
                BinaryPrimitives.ReadUInt16LittleEndian(currentValue),
                BinaryPrimitives.ReadUInt16LittleEndian(comparisonBytes),
                comparisonMode),
            ScanValueType.Int32 => MatchComparable(
                BinaryPrimitives.ReadInt32LittleEndian(currentValue),
                BinaryPrimitives.ReadInt32LittleEndian(comparisonBytes),
                comparisonMode),
            ScanValueType.UInt32 => MatchComparable(
                BinaryPrimitives.ReadUInt32LittleEndian(currentValue),
                BinaryPrimitives.ReadUInt32LittleEndian(comparisonBytes),
                comparisonMode),
            ScanValueType.Int64 => MatchComparable(
                BinaryPrimitives.ReadInt64LittleEndian(currentValue),
                BinaryPrimitives.ReadInt64LittleEndian(comparisonBytes),
                comparisonMode),
            ScanValueType.UInt64 => MatchComparable(
                BinaryPrimitives.ReadUInt64LittleEndian(currentValue),
                BinaryPrimitives.ReadUInt64LittleEndian(comparisonBytes),
                comparisonMode),
            ScanValueType.Float => MatchFloating(
                BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(currentValue)),
                BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(comparisonBytes)),
                comparisonMode,
                floatingPointTolerance),
            ScanValueType.Double => MatchFloating(
                BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64LittleEndian(currentValue)),
                BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64LittleEndian(comparisonBytes)),
                comparisonMode,
                floatingPointTolerance),
            _ => false,
        };
    }

    private static bool MatchComparable<T>(
        T current,
        T comparison,
        ScanComparisonMode mode)
        where T : IComparable<T>
    {
        var order = current.CompareTo(comparison);

        return mode switch
        {
            ScanComparisonMode.ExactValue or
            ScanComparisonMode.Unchanged => order == 0,
            ScanComparisonMode.Changed => order != 0,
            ScanComparisonMode.Increased or
            ScanComparisonMode.GreaterThan => order > 0,
            ScanComparisonMode.Decreased or
            ScanComparisonMode.LessThan => order < 0,
            _ => false,
        };
    }

    private static bool MatchFloating(
        double current,
        double comparison,
        ScanComparisonMode mode,
        double tolerance)
    {
        var equal = FloatingEquals(
            current,
            comparison,
            tolerance);
        var canOrder =
            !double.IsNaN(current) &&
            !double.IsNaN(comparison);

        return mode switch
        {
            ScanComparisonMode.ExactValue or
            ScanComparisonMode.Unchanged => equal,
            ScanComparisonMode.Changed => !equal,
            ScanComparisonMode.Increased =>
                canOrder && !equal && current > comparison,
            ScanComparisonMode.Decreased =>
                canOrder && !equal && current < comparison,
            ScanComparisonMode.GreaterThan =>
                canOrder && current > comparison,
            ScanComparisonMode.LessThan =>
                canOrder && current < comparison,
            _ => false,
        };
    }

    private static bool FloatingEquals(
        double current,
        double comparison,
        double tolerance)
    {
        if (double.IsNaN(current) || double.IsNaN(comparison))
        {
            return double.IsNaN(current) &&
                   double.IsNaN(comparison);
        }

        if (double.IsInfinity(current) ||
            double.IsInfinity(comparison))
        {
            return current.Equals(comparison);
        }

        return Math.Abs(current - comparison) <= tolerance;
    }

    private static Result<bool> Invalid(
        string message,
        Exception? exception = null)
    {
        return Result<bool>.Failure(
            new Error(
                ErrorCode.Validation,
                message,
                exception));
    }

    private static Result ValidateMatcher(
        ScanValueType valueType,
        ScanComparisonMode comparisonMode,
        double floatingPointTolerance)
    {
        try
        {
            _ = ScanValueTypeInfo.GetSize(valueType);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "The comparison value type is invalid.",
                    exception));
        }

        if (!Enum.IsDefined(comparisonMode) ||
            comparisonMode ==
            ScanComparisonMode.UnknownInitialValue)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "The comparison mode cannot match two values."));
        }

        if (!double.IsFinite(floatingPointTolerance) ||
            floatingPointTolerance < 0)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Floating-point tolerance must be finite and non-negative."));
        }

        if (!ScanValueTypeInfo.IsFloatingPoint(valueType) &&
            floatingPointTolerance != 0)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Integer comparisons do not use floating-point tolerance."));
        }

        return Result.Success();
    }
}

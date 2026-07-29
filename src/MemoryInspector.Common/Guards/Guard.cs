using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MemoryInspector.Common;

public static class Guard
{
    public static T NotNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        return value ?? throw new ArgumentNullException(parameterName);
    }

    public static string NotNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Value cannot be null, empty, or whitespace.",
                parameterName);
        }

        return value;
    }

    public static int Positive(
        int value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Value must be greater than zero.");
        }

        return value;
    }

    public static long Positive(
        long value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Value must be greater than zero.");
        }

        return value;
    }

    public static long NonNegative(
        long value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Value cannot be negative.");
        }

        return value;
    }

    public static T InRange<T>(
        T value,
        T minimum,
        T maximum,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where T : IComparable<T>
    {
        if (minimum.CompareTo(maximum) > 0)
        {
            throw new ArgumentException(
                "Minimum cannot be greater than maximum.",
                nameof(minimum));
        }

        if (value.CompareTo(minimum) < 0 || value.CompareTo(maximum) > 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Value must be between {minimum} and {maximum}, inclusive.");
        }

        return value;
    }
}

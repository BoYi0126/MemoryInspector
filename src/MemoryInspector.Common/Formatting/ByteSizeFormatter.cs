using System.Globalization;

namespace MemoryInspector.Common;

public static class ByteSizeFormatter
{
    private const double UnitScale = 1024d;

    private static readonly string[] Units =
    [
        "B",
        "KB",
        "MB",
        "GB",
        "TB",
        "PB",
        "EB",
    ];

    public static string Format(
        long bytes,
        int decimalPlaces = 2,
        IFormatProvider? formatProvider = null)
    {
        Guard.NonNegative(bytes);
        Guard.InRange(decimalPlaces, 0, 6);

        if (bytes < (long)UnitScale)
        {
            return string.Create(
                formatProvider ?? CultureInfo.InvariantCulture,
                $"{bytes} B");
        }

        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= UnitScale && unitIndex < Units.Length - 1)
        {
            value /= UnitScale;
            unitIndex++;
        }

        var numberFormat = decimalPlaces == 0
            ? "0"
            : $"0.{new string('#', decimalPlaces)}";

        return string.Concat(
            value.ToString(
                numberFormat,
                formatProvider ?? CultureInfo.InvariantCulture),
            " ",
            Units[unitIndex]);
    }
}

using System.Globalization;

namespace MemoryInspector.Common;

public static class HexAddressFormatter
{
    public static string Format(ulong address)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"0x{address:X16}");
    }
}

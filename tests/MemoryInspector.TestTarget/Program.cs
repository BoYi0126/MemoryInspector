using System.Globalization;
using System.Runtime.InteropServices;

var memory = Marshal.AllocHGlobal(16);
var intAddress = memory;
var floatAddress = memory + 8;

try
{
    Marshal.WriteInt32(intAddress, 123_456_789);
    Marshal.WriteInt32(
        floatAddress,
        BitConverter.SingleToInt32Bits(12.5F));
    Console.WriteLine(
        $"READY|{Environment.ProcessId}|" +
        $"{(ulong)(nuint)intAddress:X}|" +
        $"{(ulong)(nuint)floatAddress:X}");

    while (Console.ReadLine() is { } command)
    {
        var parts = command.Split('|', 2);

        switch (parts[0].ToUpperInvariant())
        {
            case "GET":
                var integer = Marshal.ReadInt32(intAddress);
                var floating = BitConverter.Int32BitsToSingle(
                    Marshal.ReadInt32(floatAddress));
                Console.WriteLine(
                    $"VALUES|{integer}|" +
                    floating.ToString(
                        "R",
                        CultureInfo.InvariantCulture));
                break;

            case "SETINT" when
                parts.Length == 2 &&
                int.TryParse(
                    parts[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var newInteger):
                Marshal.WriteInt32(intAddress, newInteger);
                Console.WriteLine("OK");
                break;

            case "SETFLOAT" when
                parts.Length == 2 &&
                float.TryParse(
                    parts[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var newFloat):
                Marshal.WriteInt32(
                    floatAddress,
                    BitConverter.SingleToInt32Bits(newFloat));
                Console.WriteLine("OK");
                break;

            case "EXIT":
                Console.WriteLine("BYE");
                return;

            default:
                Console.WriteLine("ERROR");
                break;
        }
    }
}
finally
{
    Marshal.FreeHGlobal(memory);
}

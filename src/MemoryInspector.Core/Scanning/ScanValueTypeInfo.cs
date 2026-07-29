namespace MemoryInspector.Core.Scanning;

public static class ScanValueTypeInfo
{
    public static int GetSize(ScanValueType valueType)
    {
        return valueType switch
        {
            ScanValueType.Byte => sizeof(byte),
            ScanValueType.Int16 => sizeof(short),
            ScanValueType.UInt16 => sizeof(ushort),
            ScanValueType.Int32 => sizeof(int),
            ScanValueType.UInt32 => sizeof(uint),
            ScanValueType.Int64 => sizeof(long),
            ScanValueType.UInt64 => sizeof(ulong),
            ScanValueType.Float => sizeof(float),
            ScanValueType.Double => sizeof(double),
            _ => throw new ArgumentOutOfRangeException(
                nameof(valueType),
                valueType,
                "Unknown scan value type."),
        };
    }

    public static bool IsFloatingPoint(ScanValueType valueType)
    {
        return valueType is
            ScanValueType.Float or
            ScanValueType.Double;
    }
}

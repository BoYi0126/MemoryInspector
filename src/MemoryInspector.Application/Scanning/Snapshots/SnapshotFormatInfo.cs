namespace MemoryInspector.Application.Scanning.Snapshots;

public static class SnapshotFormatInfo
{
    public const int CurrentVersion = 1;
    public const int HeaderSize = 128;
    public const int AddressSize = sizeof(ulong);
}

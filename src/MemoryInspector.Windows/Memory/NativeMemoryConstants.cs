namespace MemoryInspector.Windows.Memory;

internal static class NativeMemoryConstants
{
    public const uint ProcessQueryInformation = 0x0400;
    public const uint ProcessVmOperation = 0x0008;
    public const uint ProcessVmRead = 0x0010;
    public const uint ProcessVmWrite = 0x0020;

    public const uint MemCommit = 0x1000;
    public const uint MemReserve = 0x2000;
    public const uint MemFree = 0x10000;
    public const uint MemPrivate = 0x20000;
    public const uint MemMapped = 0x40000;
    public const uint MemImage = 0x1000000;

    public const uint PageNoAccess = 0x01;
    public const uint PageReadOnly = 0x02;
    public const uint PageReadWrite = 0x04;
    public const uint PageWriteCopy = 0x08;
    public const uint PageExecute = 0x10;
    public const uint PageExecuteRead = 0x20;
    public const uint PageExecuteReadWrite = 0x40;
    public const uint PageExecuteWriteCopy = 0x80;
    public const uint PageGuard = 0x100;
    public const uint PageNoCache = 0x200;
    public const uint PageWriteCombine = 0x400;

    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidHandle = 6;
    public const int ErrorInvalidParameter = 87;
    public const int ErrorPartialCopy = 299;
    public const int ErrorInvalidAddress = 487;
    public const int ErrorNoAccess = 998;
}

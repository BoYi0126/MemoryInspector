namespace MemoryInspector.Windows.Memory.Editing;

internal interface IMemoryWriterNativeApi
{
    WindowsProcessWriteHandle OpenProcess(int processId);

    bool TryQuery(
        WindowsProcessWriteHandle processHandle,
        ulong address,
        out NativeMemoryRegion region,
        out int errorCode);

    bool TryRead(
        WindowsProcessWriteHandle processHandle,
        ulong address,
        byte[] buffer,
        out int bytesRead,
        out int errorCode);

    bool TryWrite(
        WindowsProcessWriteHandle processHandle,
        ulong address,
        byte[] buffer,
        out int bytesWritten,
        out int errorCode);
}

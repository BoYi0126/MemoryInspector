using Microsoft.Win32.SafeHandles;

namespace MemoryInspector.Windows.Memory;

internal interface IMemoryReaderNativeApi
{
    SafeProcessHandle OpenProcess(int processId);

    bool TryRead(
        SafeProcessHandle processHandle,
        ulong address,
        byte[] buffer,
        out int bytesRead,
        out int errorCode);
}

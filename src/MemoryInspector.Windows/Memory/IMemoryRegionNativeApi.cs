using Microsoft.Win32.SafeHandles;

namespace MemoryInspector.Windows.Memory;

internal interface IMemoryRegionNativeApi
{
    ulong MaximumApplicationAddress { get; }

    SafeProcessHandle OpenProcess(int processId);

    bool TryQuery(
        SafeProcessHandle processHandle,
        ulong address,
        out NativeMemoryRegion region,
        out int errorCode);
}

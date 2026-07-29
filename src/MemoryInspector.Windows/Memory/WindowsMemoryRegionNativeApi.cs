using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MemoryInspector.Windows.Memory;

internal sealed class WindowsMemoryRegionNativeApi : IMemoryRegionNativeApi
{
    public ulong MaximumApplicationAddress
    {
        get
        {
            GetNativeSystemInfo(out var systemInfo);
            return (ulong)(nuint)systemInfo.MaximumApplicationAddress;
        }
    }

    public SafeProcessHandle OpenProcess(int processId)
    {
        var handle = OpenProcessNative(
            NativeMemoryConstants.ProcessQueryInformation,
            inheritHandle: false,
            processId);

        if (handle.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(errorCode);
        }

        return handle;
    }

    public bool TryQuery(
        SafeProcessHandle processHandle,
        ulong address,
        out NativeMemoryRegion region,
        out int errorCode)
    {
        var bytesWritten = VirtualQueryEx(
            processHandle,
            (nuint)address,
            out var information,
            (nuint)Marshal.SizeOf<MemoryBasicInformation64>());

        if (bytesWritten == 0)
        {
            region = default;
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        region = new NativeMemoryRegion(
            information.BaseAddress,
            information.AllocationBase,
            information.RegionSize,
            information.State,
            information.Protect,
            information.Type);
        errorCode = 0;
        return true;
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcessNative(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQueryEx(
        SafeProcessHandle processHandle,
        nuint address,
        out MemoryBasicInformation64 buffer,
        nuint length);

    [DllImport("kernel32.dll")]
    private static extern void GetNativeSystemInfo(
        out SystemInfo systemInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation64
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public uint AllocationProtect;
        public uint Alignment1;
        public ulong RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint Alignment2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemInfo
    {
        public ushort ProcessorArchitecture;
        public ushort Reserved;
        public uint PageSize;
        public nint MinimumApplicationAddress;
        public nint MaximumApplicationAddress;
        public nuint ActiveProcessorMask;
        public uint NumberOfProcessors;
        public uint ProcessorType;
        public uint AllocationGranularity;
        public ushort ProcessorLevel;
        public ushort ProcessorRevision;
    }
}

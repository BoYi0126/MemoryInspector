using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MemoryInspector.Windows.Memory.Editing;

internal sealed class WindowsMemoryWriterNativeApi
    : IMemoryWriterNativeApi
{
    public WindowsProcessWriteHandle OpenProcess(int processId)
    {
        var handle = OpenProcessNative(
            NativeMemoryConstants.ProcessQueryInformation |
            NativeMemoryConstants.ProcessVmOperation |
            NativeMemoryConstants.ProcessVmRead |
            NativeMemoryConstants.ProcessVmWrite,
            inheritHandle: false,
            processId);

        if (handle.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(errorCode);
        }

        return new WindowsProcessWriteHandle(handle);
    }

    public bool TryQuery(
        WindowsProcessWriteHandle processHandle,
        ulong address,
        out NativeMemoryRegion region,
        out int errorCode)
    {
        ArgumentNullException.ThrowIfNull(processHandle);
        var bytesWritten = VirtualQueryEx(
            processHandle.NativeHandle,
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

    public bool TryRead(
        WindowsProcessWriteHandle processHandle,
        ulong address,
        byte[] buffer,
        out int bytesRead,
        out int errorCode)
    {
        ArgumentNullException.ThrowIfNull(processHandle);
        ArgumentNullException.ThrowIfNull(buffer);
        var success = ReadProcessMemory(
            processHandle.NativeHandle,
            (nuint)address,
            buffer,
            (nuint)buffer.Length,
            out var nativeBytesRead);
        bytesRead = ToManagedByteCount(nativeBytesRead);
        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    public bool TryWrite(
        WindowsProcessWriteHandle processHandle,
        ulong address,
        byte[] buffer,
        out int bytesWritten,
        out int errorCode)
    {
        ArgumentNullException.ThrowIfNull(processHandle);
        ArgumentNullException.ThrowIfNull(buffer);
        var success = WriteProcessMemory(
            processHandle.NativeHandle,
            (nuint)address,
            buffer,
            (nuint)buffer.Length,
            out var nativeBytesWritten);
        bytesWritten = ToManagedByteCount(nativeBytesWritten);
        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    private static int ToManagedByteCount(nuint nativeByteCount)
    {
        return nativeByteCount > int.MaxValue
            ? int.MaxValue
            : (int)nativeByteCount;
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "OpenProcess",
        SetLastError = true)]
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        SafeProcessHandle processHandle,
        nuint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint numberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        SafeProcessHandle processHandle,
        nuint baseAddress,
        byte[] buffer,
        nuint size,
        out nuint numberOfBytesWritten);

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
}

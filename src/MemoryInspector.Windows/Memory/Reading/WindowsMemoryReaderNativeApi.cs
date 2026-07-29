using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MemoryInspector.Windows.Memory;

internal sealed class WindowsMemoryReaderNativeApi
    : IMemoryReaderNativeApi
{
    public SafeProcessHandle OpenProcess(int processId)
    {
        var handle = OpenProcessNative(
            NativeMemoryConstants.ProcessQueryInformation |
            NativeMemoryConstants.ProcessVmRead,
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

    public bool TryRead(
        SafeProcessHandle processHandle,
        ulong address,
        byte[] buffer,
        out int bytesRead,
        out int errorCode)
    {
        var success = ReadProcessMemory(
            processHandle,
            (nuint)address,
            buffer,
            (nuint)buffer.Length,
            out var nativeBytesRead);
        bytesRead = nativeBytesRead > int.MaxValue
            ? int.MaxValue
            : (int)nativeBytesRead;
        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcessNative(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        SafeProcessHandle processHandle,
        nuint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint numberOfBytesRead);
}

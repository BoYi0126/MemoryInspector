using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.Windows.Processes;

internal static class ProcessArchitectureDetector
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const ushort ImageFileMachineUnknown = 0x0000;
    private const ushort ImageFileMachineI386 = 0x014c;
    private const ushort ImageFileMachineArmNt = 0x01c4;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xaa64;

    public static ProcessArchitecture Detect(int processId)
    {
        using var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);

        if (processHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            if (!IsWow64Process2(
                processHandle,
                out var processMachine,
                out var nativeMachine))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var machine = processMachine == ImageFileMachineUnknown
                ? nativeMachine
                : processMachine;

            return MapMachine(machine);
        }
        catch (EntryPointNotFoundException)
        {
            return DetectWithLegacyApi(processHandle);
        }
    }

    private static ProcessArchitecture DetectWithLegacyApi(
        SafeProcessHandle processHandle)
    {
        if (!IsWow64Process(processHandle, out var isWow64))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!Environment.Is64BitOperatingSystem)
        {
            return ProcessArchitecture.X86;
        }

        return isWow64
            ? ProcessArchitecture.X86
            : ProcessArchitecture.X64;
    }

    private static ProcessArchitecture MapMachine(ushort machine)
    {
        return machine switch
        {
            ImageFileMachineI386 => ProcessArchitecture.X86,
            ImageFileMachineAmd64 => ProcessArchitecture.X64,
            ImageFileMachineArmNt => ProcessArchitecture.Arm32,
            ImageFileMachineArm64 => ProcessArchitecture.Arm64,
            _ => ProcessArchitecture.Unknown,
        };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(
        SafeProcessHandle processHandle,
        out ushort processMachine,
        out ushort nativeMachine);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(
        SafeProcessHandle processHandle,
        [MarshalAs(UnmanagedType.Bool)] out bool isWow64);
}

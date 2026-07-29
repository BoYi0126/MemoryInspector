using System.Diagnostics;

namespace MemoryInspector.Windows.ProcessInspection;

internal sealed class SystemProcessDetailsSource :
    IWindowsProcessDetailsSource
{
    public IEnumerable<IWindowsModuleAccessor> EnumerateModules(
        int processId)
    {
        using var process = Process.GetProcessById(processId);

        foreach (ProcessModule module in process.Modules)
        {
            using (module)
            {
                yield return new SystemModuleAccessor(module);
            }
        }
    }

    public IEnumerable<IWindowsThreadAccessor> EnumerateThreads(
        int processId)
    {
        using var process = Process.GetProcessById(processId);

        foreach (ProcessThread thread in process.Threads)
        {
            using (thread)
            {
                yield return new SystemThreadAccessor(thread);
            }
        }
    }

    private sealed class SystemModuleAccessor(
        ProcessModule module) : IWindowsModuleAccessor
    {
        public string GetName() => module.ModuleName;

        public nint GetBaseAddress() => module.BaseAddress;

        public int GetSize() => module.ModuleMemorySize;

        public string GetPath() => module.FileName;

        public string? GetVersion() =>
            module.FileVersionInfo.FileVersion;
    }

    private sealed class SystemThreadAccessor(
        ProcessThread thread) : IWindowsThreadAccessor
    {
        public int GetThreadId() => thread.Id;

        public string GetState() =>
            thread.ThreadState.ToString();

        public int GetPriority() => thread.BasePriority;

        public DateTimeOffset GetStartTime() =>
            new(thread.StartTime);

        public TimeSpan GetCpuTime() =>
            thread.TotalProcessorTime;
    }
}

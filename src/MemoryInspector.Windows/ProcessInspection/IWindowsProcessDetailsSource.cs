namespace MemoryInspector.Windows.ProcessInspection;

internal interface IWindowsProcessDetailsSource
{
    IEnumerable<IWindowsModuleAccessor> EnumerateModules(
        int processId);

    IEnumerable<IWindowsThreadAccessor> EnumerateThreads(
        int processId);
}

internal interface IWindowsModuleAccessor
{
    string GetName();

    nint GetBaseAddress();

    int GetSize();

    string GetPath();

    string? GetVersion();
}

internal interface IWindowsThreadAccessor
{
    int GetThreadId();

    string GetState();

    int GetPriority();

    DateTimeOffset GetStartTime();

    TimeSpan GetCpuTime();
}

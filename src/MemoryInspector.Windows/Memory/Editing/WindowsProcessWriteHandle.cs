using Microsoft.Win32.SafeHandles;

namespace MemoryInspector.Windows.Memory.Editing;

public sealed class WindowsProcessWriteHandle : IDisposable
{
    private readonly SafeProcessHandle _handle;

    internal WindowsProcessWriteHandle(SafeProcessHandle handle)
    {
        _handle = handle ??
            throw new ArgumentNullException(nameof(handle));

        if (handle.IsInvalid)
        {
            throw new ArgumentException(
                "A valid process handle is required.",
                nameof(handle));
        }
    }

    public bool IsClosed => _handle.IsClosed;

    internal SafeProcessHandle NativeHandle => _handle;

    public void Dispose()
    {
        _handle.Dispose();
    }
}

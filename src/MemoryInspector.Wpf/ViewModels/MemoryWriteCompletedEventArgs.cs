using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class MemoryWriteCompletedEventArgs(
    MemoryWriteRequest request,
    MemoryWriteResult result) : EventArgs
{
    public MemoryWriteRequest Request { get; } =
        request ?? throw new ArgumentNullException(nameof(request));

    public MemoryWriteResult Result { get; } =
        result ?? throw new ArgumentNullException(nameof(result));
}

namespace MemoryInspector.Application.Memory.Editing;

public sealed class MemoryEditorFeatureChangedEventArgs(
    MemoryEditorFeatureState state) : EventArgs
{
    public MemoryEditorFeatureState State { get; } =
        state ?? throw new ArgumentNullException(nameof(state));
}

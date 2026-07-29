namespace MemoryInspector.Wpf.ViewModels;

public sealed class ResultAddressActionRequestedEventArgs(
    ResultGridRowViewModel row) : EventArgs
{
    public ResultGridRowViewModel Row { get; } =
        row ?? throw new ArgumentNullException(nameof(row));
}

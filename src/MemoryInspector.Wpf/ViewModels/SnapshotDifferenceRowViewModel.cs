using MemoryInspector.Application.Scanning.Snapshots.Comparison;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class SnapshotDifferenceRowViewModel
{
    public SnapshotDifferenceRowViewModel(
        SnapshotDifference difference)
    {
        Difference = difference ??
            throw new ArgumentNullException(nameof(difference));
    }

    public SnapshotDifference Difference { get; }

    public ulong Address => Difference.Address;

    public string AddressDisplay => $"0x{Address:X16}";

    public SnapshotDifferenceKind Kind => Difference.Kind;

    public string LeftValueDisplay =>
        FormatValue(Difference.LeftValue);

    public string RightValueDisplay =>
        FormatValue(Difference.RightValue);

    private static string FormatValue(
        ReadOnlyMemory<byte>? value)
    {
        if (!value.HasValue)
        {
            return "—";
        }

        return value.Value.IsEmpty
            ? "(address only)"
            : Convert.ToHexString(value.Value.Span);
    }
}

namespace MemoryInspector.Application.Scanning.Snapshots.Comparison;

public sealed class SnapshotDifference
{
    private readonly byte[]? _leftValue;
    private readonly byte[]? _rightValue;

    public SnapshotDifference(
        ulong address,
        SnapshotDifferenceKind kind,
        ReadOnlyMemory<byte>? leftValue,
        ReadOnlyMemory<byte>? rightValue)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if ((kind == SnapshotDifferenceKind.Added &&
             (leftValue.HasValue || !rightValue.HasValue)) ||
            (kind == SnapshotDifferenceKind.Removed &&
             (!leftValue.HasValue || rightValue.HasValue)) ||
            (kind is SnapshotDifferenceKind.Changed or
                SnapshotDifferenceKind.Unchanged &&
             (!leftValue.HasValue || !rightValue.HasValue)))
        {
            throw new ArgumentException(
                "Difference values do not match the difference kind.");
        }

        Address = address;
        Kind = kind;
        _leftValue = leftValue?.ToArray();
        _rightValue = rightValue?.ToArray();
    }

    public ulong Address { get; }

    public SnapshotDifferenceKind Kind { get; }

    public ReadOnlyMemory<byte>? LeftValue => _leftValue;

    public ReadOnlyMemory<byte>? RightValue => _rightValue;
}

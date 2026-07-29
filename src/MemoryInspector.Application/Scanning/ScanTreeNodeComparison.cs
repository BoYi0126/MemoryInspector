namespace MemoryInspector.Application.Scanning;

public sealed record ScanTreeNodeComparison
{
    public ScanTreeNodeComparison(
        ScanTreeNode left,
        ScanTreeNode right,
        Guid? nearestCommonAncestorId,
        bool isLeftAncestorOfRight,
        bool isRightAncestorOfLeft)
    {
        Left = left ??
            throw new ArgumentNullException(nameof(left));
        Right = right ??
            throw new ArgumentNullException(nameof(right));

        if (nearestCommonAncestorId == Guid.Empty)
        {
            throw new ArgumentException(
                "Common ancestor ID cannot be empty.",
                nameof(nearestCommonAncestorId));
        }

        NearestCommonAncestorId = nearestCommonAncestorId;
        IsLeftAncestorOfRight = isLeftAncestorOfRight;
        IsRightAncestorOfLeft = isRightAncestorOfLeft;
    }

    public ScanTreeNode Left { get; }

    public ScanTreeNode Right { get; }

    public Guid? NearestCommonAncestorId { get; }

    public bool IsLeftAncestorOfRight { get; }

    public bool IsRightAncestorOfLeft { get; }

    public long CandidateCountDelta =>
        Right.CandidateCount - Left.CandidateCount;

    public bool UsesSameValueType =>
        Left.Round.Snapshot.ValueType ==
        Right.Round.Snapshot.ValueType;
}

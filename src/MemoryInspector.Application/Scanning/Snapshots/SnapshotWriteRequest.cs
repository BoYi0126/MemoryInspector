using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning.Snapshots;

public sealed record SnapshotWriteRequest
{
    public SnapshotWriteRequest(
        Guid sessionId,
        int nodeId,
        ScanValueType valueType,
        bool includeValues,
        long? expectedRecordCount = null)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Session ID cannot be empty.",
                nameof(sessionId));
        }

        if (nodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeId),
                nodeId,
                "Node ID must be greater than zero.");
        }

        _ = ScanValueTypeInfo.GetSize(valueType);

        if (expectedRecordCount is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedRecordCount));
        }

        SessionId = sessionId;
        NodeId = nodeId;
        ValueType = valueType;
        IncludeValues = includeValues;
        ExpectedRecordCount = expectedRecordCount;
    }

    public Guid SessionId { get; }

    public int NodeId { get; }

    public ScanValueType ValueType { get; }

    public bool IncludeValues { get; }

    public long? ExpectedRecordCount { get; }

    public int ValueSize => IncludeValues
        ? ScanValueTypeInfo.GetSize(ValueType)
        : 0;

    public int RecordSize => sizeof(ulong) + ValueSize;
}

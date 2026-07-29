using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning.Snapshots;

public sealed record SnapshotDescriptor
{
    public SnapshotDescriptor(
        Guid sessionId,
        int nodeId,
        int formatVersion,
        ScanValueType valueType,
        bool includesValues,
        int valueSize,
        int recordSize,
        long recordCount,
        long payloadLength,
        string checksum,
        DateTimeOffset createdAt,
        string filePath,
        SnapshotStorageKind storageKind =
            SnapshotStorageKind.Full,
        int? parentNodeId = null,
        int chainDepth = 0,
        long accumulatedDeltaBytes = 0,
        int referenceCount = 0)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Session ID cannot be empty.",
                nameof(sessionId));
        }

        if (nodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId));
        }

        if (formatVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(formatVersion));
        }

        _ = ScanValueTypeInfo.GetSize(valueType);

        if (valueSize < 0 ||
            recordSize != sizeof(ulong) + valueSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recordSize));
        }

        if (includesValues != (valueSize > 0))
        {
            throw new ArgumentException(
                "Value layout is inconsistent.",
                nameof(includesValues));
        }

        if (recordCount < 0 || payloadLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recordCount));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(checksum);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!Enum.IsDefined(storageKind) ||
            chainDepth < 0 ||
            accumulatedDeltaBytes < 0 ||
            referenceCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(storageKind));
        }

        if (storageKind == SnapshotStorageKind.Full)
        {
            if (parentNodeId is not null ||
                chainDepth != 0 ||
                accumulatedDeltaBytes != 0)
            {
                throw new ArgumentException(
                    "A full snapshot cannot depend on a parent.");
            }
        }
        else if (parentNodeId is null or <= 0 ||
                 parentNodeId == nodeId ||
                 chainDepth <= 0)
        {
            throw new ArgumentException(
                "A delta snapshot requires a valid parent.");
        }

        SessionId = sessionId;
        NodeId = nodeId;
        FormatVersion = formatVersion;
        ValueType = valueType;
        IncludesValues = includesValues;
        ValueSize = valueSize;
        RecordSize = recordSize;
        RecordCount = recordCount;
        PayloadLength = payloadLength;
        Checksum = checksum;
        CreatedAt = createdAt;
        FilePath = filePath;
        StorageKind = storageKind;
        ParentNodeId = parentNodeId;
        ChainDepth = chainDepth;
        AccumulatedDeltaBytes = accumulatedDeltaBytes;
        ReferenceCount = referenceCount;
    }

    public Guid SessionId { get; }

    public int NodeId { get; }

    public int FormatVersion { get; }

    public ScanValueType ValueType { get; }

    public bool IncludesValues { get; }

    public int ValueSize { get; }

    public int RecordSize { get; }

    public long RecordCount { get; }

    public long PayloadLength { get; }

    public string Checksum { get; }

    public DateTimeOffset CreatedAt { get; }

    public string FilePath { get; }

    public SnapshotStorageKind StorageKind { get; }

    public int? ParentNodeId { get; }

    public int ChainDepth { get; }

    public long AccumulatedDeltaBytes { get; }

    public int ReferenceCount { get; }

    public long FullPayloadLength =>
        checked(RecordCount * RecordSize);
}

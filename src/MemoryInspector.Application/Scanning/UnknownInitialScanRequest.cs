using MemoryInspector.Application.Memory;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed record UnknownInitialScanRequest
{
    public UnknownInitialScanRequest(
        int nodeId,
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        int chunkSizeBytes =
            FirstScanOptions.DefaultChunkSizeBytes)
    {
        if (nodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeId),
                nodeId,
                "Node ID must be greater than zero.");
        }

        _ = ScanValueTypeInfo.GetSize(valueType);

        if (!Enum.IsDefined(alignmentMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignmentMode));
        }

        if (chunkSizeBytes <= 0 ||
            chunkSizeBytes >
            MemoryReadOptions.MaximumChunkSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkSizeBytes));
        }

        if (chunkSizeBytes <
            ScanValueTypeInfo.GetSize(valueType))
        {
            throw new ArgumentException(
                "Chunk size cannot be smaller than the value size.",
                nameof(chunkSizeBytes));
        }

        NodeId = nodeId;
        ValueType = valueType;
        AlignmentMode = alignmentMode;
        ChunkSizeBytes = chunkSizeBytes;
    }

    public int NodeId { get; }

    public ScanValueType ValueType { get; }

    public ScanAlignmentMode AlignmentMode { get; }

    public int ChunkSizeBytes { get; }

    public int ValueSize => ScanValueTypeInfo.GetSize(ValueType);

    public int AddressStep =>
        AlignmentMode == ScanAlignmentMode.Aligned
            ? ValueSize
            : 1;
}

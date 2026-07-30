using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed record ExactInitialScanRequest
{
    public ExactInitialScanRequest(
        int nodeId,
        ScanRequest scanRequest,
        int chunkSizeBytes = FirstScanOptions.DefaultChunkSizeBytes)
    {
        if (nodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId));
        }

        ScanRequest = scanRequest ??
            throw new ArgumentNullException(nameof(scanRequest));

        if (scanRequest.ComparisonMode !=
                ScanComparisonMode.ExactValue ||
            scanRequest.SearchValue is null)
        {
            throw new ArgumentException(
                "Exact initial scan requires an exact search value.",
                nameof(scanRequest));
        }

        _ = new FirstScanOptions(chunkSizeBytes);
        NodeId = nodeId;
        ChunkSizeBytes = chunkSizeBytes;
    }

    public int NodeId { get; }

    public ScanRequest ScanRequest { get; }

    public int ChunkSizeBytes { get; }
}

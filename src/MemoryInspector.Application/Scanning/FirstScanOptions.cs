using MemoryInspector.Application.Memory;

namespace MemoryInspector.Application.Scanning;

public sealed record FirstScanOptions
{
    public const int DefaultChunkSizeBytes = 1024 * 1024;

    public FirstScanOptions(
        int chunkSizeBytes = DefaultChunkSizeBytes)
    {
        if (chunkSizeBytes <= 0 ||
            chunkSizeBytes >
            MemoryReadOptions.MaximumChunkSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkSizeBytes),
                chunkSizeBytes,
                $"Chunk size must be between 1 and " +
                $"{MemoryReadOptions.MaximumChunkSizeBytes:N0} bytes.");
        }

        ChunkSizeBytes = chunkSizeBytes;
    }

    public int ChunkSizeBytes { get; }
}

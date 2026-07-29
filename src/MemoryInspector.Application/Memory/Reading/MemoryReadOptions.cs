namespace MemoryInspector.Application.Memory;

public sealed record MemoryReadOptions
{
    public const int DefaultChunkSizeBytes = 64 * 1024;
    public const int MaximumChunkSizeBytes = 16 * 1024 * 1024;

    public MemoryReadOptions(
        int chunkSizeBytes = DefaultChunkSizeBytes)
    {
        if (chunkSizeBytes <= 0 ||
            chunkSizeBytes > MaximumChunkSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkSizeBytes),
                chunkSizeBytes,
                $"Chunk size must be between 1 and " +
                $"{MaximumChunkSizeBytes:N0} bytes.");
        }

        ChunkSizeBytes = chunkSizeBytes;
    }

    public int ChunkSizeBytes { get; }
}

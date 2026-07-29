namespace MemoryInspector.Application.Memory;

public sealed class MemoryBatchReadResult
{
    public MemoryBatchReadResult(
        IEnumerable<MemoryBatchReadItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = Array.AsReadOnly(items.ToArray());

        if (Items.Any(item => item is null))
        {
            throw new ArgumentException(
                "Batch read items cannot contain null.",
                nameof(items));
        }
    }

    public IReadOnlyList<MemoryBatchReadItem> Items { get; }

    public int SucceededCount =>
        Items.Count(item => item.Result.IsSuccess);

    public int FailedCount => Items.Count - SucceededCount;

    public bool IsPartial =>
        FailedCount > 0 ||
        Items.Any(item =>
            item.Result.IsSuccess &&
            item.Result.Value.IsPartial);
}

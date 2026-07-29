namespace MemoryInspector.Common;

/// <summary>
/// Represents one immutable, one-based page of a larger result set.
/// </summary>
public sealed class PagedResult<T>
{
    public PagedResult(
        IEnumerable<T> items,
        long pageNumber,
        int pageSize,
        long totalCount)
    {
        Guard.NotNull(items);
        Guard.Positive(pageNumber);
        Guard.Positive(pageSize);
        Guard.NonNegative(totalCount);

        var copiedItems = items.ToArray();
        var totalPages = CalculateTotalPages(totalCount, pageSize);

        if (copiedItems.Length > pageSize)
        {
            throw new ArgumentException(
                "The number of items cannot exceed the page size.",
                nameof(items));
        }

        if (copiedItems.LongLength > totalCount)
        {
            throw new ArgumentException(
                "The number of items cannot exceed the total count.",
                nameof(items));
        }

        if (totalPages == 0 && pageNumber != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageNumber),
                pageNumber,
                "An empty result set only supports page 1.");
        }

        if (totalPages > 0 && pageNumber > totalPages)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageNumber),
                pageNumber,
                "Page number cannot exceed the total page count.");
        }

        Items = Array.AsReadOnly(copiedItems);
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = totalPages;
    }

    public IReadOnlyList<T> Items { get; }

    public long PageNumber { get; }

    public int PageSize { get; }

    public long TotalCount { get; }

    public long TotalPages { get; }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    private static long CalculateTotalPages(long totalCount, int pageSize)
    {
        var fullPages = totalCount / pageSize;
        return totalCount % pageSize == 0 ? fullPages : fullPages + 1;
    }
}

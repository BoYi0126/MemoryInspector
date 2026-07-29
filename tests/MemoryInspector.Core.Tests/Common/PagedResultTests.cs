using MemoryInspector.Common;

namespace MemoryInspector.Core.Tests.Common;

[TestClass]
public sealed class PagedResultTests
{
    [TestMethod]
    public void CalculatesPageBoundaries()
    {
        var page = new PagedResult<int>(
            Enumerable.Range(11, 10),
            pageNumber: 2,
            pageSize: 10,
            totalCount: 25);

        Assert.AreEqual(3L, page.TotalPages);
        Assert.IsTrue(page.HasPreviousPage);
        Assert.IsTrue(page.HasNextPage);
        Assert.AreEqual(10, page.Items.Count);
    }

    [TestMethod]
    public void EmptyResultUsesPageOneAndHasNoNavigation()
    {
        var page = new PagedResult<int>(
            Array.Empty<int>(),
            pageNumber: 1,
            pageSize: 100,
            totalCount: 0);

        Assert.AreEqual(0L, page.TotalPages);
        Assert.IsFalse(page.HasPreviousPage);
        Assert.IsFalse(page.HasNextPage);
    }

    [TestMethod]
    public void RejectsPageBeyondLastPage()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => _ = new PagedResult<int>(
                Array.Empty<int>(),
                pageNumber: 3,
                pageSize: 10,
                totalCount: 20));
    }

    [TestMethod]
    public void RejectsMoreItemsThanPageSize()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => _ = new PagedResult<int>(
                Enumerable.Range(1, 3),
                pageNumber: 1,
                pageSize: 2,
                totalCount: 3));
    }

    [TestMethod]
    public void TotalPageCalculationDoesNotOverflow()
    {
        var page = new PagedResult<int>(
            Array.Empty<int>(),
            pageNumber: 1,
            pageSize: 2,
            totalCount: long.MaxValue);

        Assert.AreEqual(4_611_686_018_427_387_904L, page.TotalPages);
    }
}

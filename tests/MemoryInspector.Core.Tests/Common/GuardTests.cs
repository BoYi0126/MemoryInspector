using MemoryInspector.Common;

namespace MemoryInspector.Core.Tests.Common;

[TestClass]
public sealed class GuardTests
{
    [TestMethod]
    public void NotNullReturnsTheInput()
    {
        var value = new object();

        Assert.AreSame(value, Guard.NotNull(value));
    }

    [TestMethod]
    public void NotNullRejectsNull()
    {
        object? value = null;

        Assert.ThrowsExactly<ArgumentNullException>(() => Guard.NotNull(value));
    }

    [TestMethod]
    public void NotNullOrWhiteSpaceRejectsWhitespace()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => Guard.NotNullOrWhiteSpace("  "));
    }

    [TestMethod]
    public void NumericGuardsEnforceBoundaries()
    {
        Assert.AreEqual(1, Guard.Positive(1));
        Assert.AreEqual(0L, Guard.NonNegative(0));
        Assert.AreEqual(5, Guard.InRange(5, 1, 5));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Guard.Positive(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Guard.NonNegative(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Guard.InRange(6, 1, 5));
    }
}

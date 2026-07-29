using MemoryInspector.Common;

namespace MemoryInspector.Core.Tests.Common;

[TestClass]
public sealed class FormatterTests
{
    [TestMethod]
    [DataRow(0L, "0 B")]
    [DataRow(1023L, "1023 B")]
    [DataRow(1024L, "1 KB")]
    [DataRow(1536L, "1.5 KB")]
    [DataRow(1_048_576L, "1 MB")]
    public void ByteSizeFormatterUsesBinaryUnits(long bytes, string expected)
    {
        Assert.AreEqual(expected, ByteSizeFormatter.Format(bytes));
    }

    [TestMethod]
    public void ByteSizeFormatterRejectsNegativeValues()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => ByteSizeFormatter.Format(-1));
    }

    [TestMethod]
    public void HexAddressFormatterUsesFixedWidthX64Address()
    {
        Assert.AreEqual(
            "0x0000000000000000",
            HexAddressFormatter.Format(0));
        Assert.AreEqual(
            "0x000000001234ABCD",
            HexAddressFormatter.Format(0x1234ABCD));
        Assert.AreEqual(
            "0xFFFFFFFFFFFFFFFF",
            HexAddressFormatter.Format(ulong.MaxValue));
    }
}

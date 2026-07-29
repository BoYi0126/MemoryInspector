using MemoryInspector.Core.Memory;

namespace MemoryInspector.Core.Tests.Memory;

[TestClass]
public sealed class MemoryRegionTests
{
    [TestMethod]
    public void RegionUsesX64AddressesAndAnExclusiveEndAddress()
    {
        const ulong baseAddress = 0x0000_0001_0000_0000;
        var region = new MemoryRegion(
            baseAddress,
            0x2_000,
            baseAddress,
            MemoryRegionState.Committed,
            MemoryRegionType.Private,
            MemoryProtection.ReadWrite);

        Assert.AreEqual(baseAddress, region.BaseAddress);
        Assert.AreEqual(baseAddress + 0x2_000, region.EndAddress);
        Assert.AreEqual(0x2_000UL, region.Size);
        Assert.AreEqual(baseAddress, region.AllocationBase);
    }

    [TestMethod]
    public void ReadWriteRegionExposesExpectedCapabilities()
    {
        var region = Create(MemoryProtection.ReadWrite);

        Assert.IsTrue(region.IsReadable);
        Assert.IsTrue(region.IsWritable);
        Assert.IsFalse(region.IsExecutable);
        Assert.IsFalse(region.IsGuard);
        Assert.IsFalse(region.IsNoAccess);
    }

    [TestMethod]
    public void ExecuteReadRegionIsReadableAndExecutable()
    {
        var region = Create(MemoryProtection.ExecuteRead);

        Assert.IsTrue(region.IsReadable);
        Assert.IsFalse(region.IsWritable);
        Assert.IsTrue(region.IsExecutable);
    }

    [TestMethod]
    public void GuardRegionIsNotReportedAsAccessible()
    {
        var region = Create(
            MemoryProtection.ExecuteReadWrite |
            MemoryProtection.Guard);

        Assert.IsTrue(region.IsGuard);
        Assert.IsFalse(region.IsReadable);
        Assert.IsFalse(region.IsWritable);
        Assert.IsFalse(region.IsExecutable);
    }

    [TestMethod]
    public void NoAccessRegionIsNotReportedAsAccessible()
    {
        var region = Create(MemoryProtection.NoAccess);

        Assert.IsTrue(region.IsNoAccess);
        Assert.IsFalse(region.IsReadable);
        Assert.IsFalse(region.IsWritable);
        Assert.IsFalse(region.IsExecutable);
    }

    [TestMethod]
    public void ReservedRegionIsNotReportedAsAccessible()
    {
        var region = new MemoryRegion(
            0x1_000,
            0x1_000,
            0x1_000,
            MemoryRegionState.Reserved,
            MemoryRegionType.None,
            MemoryProtection.ReadWrite);

        Assert.IsFalse(region.IsReadable);
        Assert.IsFalse(region.IsWritable);
        Assert.IsFalse(region.IsExecutable);
    }

    [TestMethod]
    public void RegionRejectsZeroSizeAndAddressOverflow()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new MemoryRegion(
                0,
                0,
                0,
                MemoryRegionState.Free,
                MemoryRegionType.None,
                MemoryProtection.None));
        Assert.ThrowsExactly<OverflowException>(() =>
            new MemoryRegion(
                ulong.MaxValue,
                1,
                0,
                MemoryRegionState.Committed,
                MemoryRegionType.Private,
                MemoryProtection.ReadOnly));
    }

    private static MemoryRegion Create(MemoryProtection protection)
    {
        return new MemoryRegion(
            0x1_000,
            0x1_000,
            0x1_000,
            MemoryRegionState.Committed,
            MemoryRegionType.Private,
            protection);
    }
}

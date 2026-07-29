using MemoryInspector.Core.Memory;
using MemoryInspector.Windows.Memory;

namespace MemoryInspector.Windows.Tests.Memory;

[TestClass]
public sealed class MemoryRegionMapperTests
{
    [TestMethod]
    public void MapsStateAndTypeValues()
    {
        Assert.AreEqual(
            MemoryRegionState.Committed,
            MemoryRegionMapper.MapState(
                NativeMemoryConstants.MemCommit));
        Assert.AreEqual(
            MemoryRegionState.Reserved,
            MemoryRegionMapper.MapState(
                NativeMemoryConstants.MemReserve));
        Assert.AreEqual(
            MemoryRegionState.Free,
            MemoryRegionMapper.MapState(
                NativeMemoryConstants.MemFree));
        Assert.AreEqual(
            MemoryRegionType.Private,
            MemoryRegionMapper.MapType(
                NativeMemoryConstants.MemPrivate));
        Assert.AreEqual(
            MemoryRegionType.Mapped,
            MemoryRegionMapper.MapType(
                NativeMemoryConstants.MemMapped));
        Assert.AreEqual(
            MemoryRegionType.Image,
            MemoryRegionMapper.MapType(
                NativeMemoryConstants.MemImage));
    }

    [TestMethod]
    public void MapsProtectionAndModifierFlags()
    {
        var protection = MemoryRegionMapper.MapProtection(
            NativeMemoryConstants.PageExecuteReadWrite |
            NativeMemoryConstants.PageGuard |
            NativeMemoryConstants.PageNoCache);

        Assert.IsTrue(
            protection.HasFlag(MemoryProtection.ExecuteReadWrite));
        Assert.IsTrue(
            protection.HasFlag(MemoryProtection.Guard));
        Assert.IsTrue(
            protection.HasFlag(MemoryProtection.NoCache));
    }

    [TestMethod]
    public void MapsGuardAndNoAccessAsInaccessible()
    {
        var noAccess = MemoryRegionMapper.Map(
            CreateNative(NativeMemoryConstants.PageNoAccess));
        var guard = MemoryRegionMapper.Map(
            CreateNative(
                NativeMemoryConstants.PageReadWrite |
                NativeMemoryConstants.PageGuard));

        Assert.IsTrue(noAccess.IsNoAccess);
        Assert.IsFalse(noAccess.IsReadable);
        Assert.IsTrue(guard.IsGuard);
        Assert.IsFalse(guard.IsReadable);
        Assert.IsFalse(guard.IsWritable);
    }

    [TestMethod]
    public void UnknownNativeValuesRemainVisible()
    {
        Assert.AreEqual(
            MemoryRegionState.Unknown,
            MemoryRegionMapper.MapState(0xDEAD));
        Assert.AreEqual(
            MemoryRegionType.Unknown,
            MemoryRegionMapper.MapType(0xBEEF));
        Assert.IsTrue(
            MemoryRegionMapper
                .MapProtection(0x8000)
                .HasFlag(MemoryProtection.Unknown));
    }

    private static NativeMemoryRegion CreateNative(uint protection)
    {
        return new NativeMemoryRegion(
            0x1_000,
            0x1_000,
            0x1_000,
            NativeMemoryConstants.MemCommit,
            protection,
            NativeMemoryConstants.MemPrivate);
    }
}

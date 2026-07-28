using MemoryInspector.Windows;

namespace MemoryInspector.Windows.Tests;

[TestClass]
public sealed class WindowsAssemblyTests
{
    [TestMethod]
    public void WindowsAssemblyUsesExpectedName()
    {
        Assert.AreEqual(
            "MemoryInspector.Windows",
            typeof(WindowsAssemblyMarker).Assembly.GetName().Name);
    }
}

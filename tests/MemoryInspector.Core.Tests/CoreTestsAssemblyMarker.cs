using MemoryInspector.Core;

namespace MemoryInspector.Core.Tests;

[TestClass]
public sealed class CoreAssemblyTests
{
    [TestMethod]
    public void CoreAssemblyUsesExpectedName()
    {
        Assert.AreEqual("MemoryInspector.Core", typeof(CoreAssemblyMarker).Assembly.GetName().Name);
    }

    [TestMethod]
    public void CoreAssemblyDoesNotReferencePlatformOrUiLayers()
    {
        var forbiddenReferences = typeof(CoreAssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is "MemoryInspector.Windows" or "MemoryInspector.Wpf")
            .Select(reference => reference.Name)
            .ToArray();

        Assert.AreEqual(0, forbiddenReferences.Length);
    }
}

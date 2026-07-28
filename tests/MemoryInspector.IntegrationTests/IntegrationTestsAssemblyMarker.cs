using MemoryInspector.Wpf;

namespace MemoryInspector.IntegrationTests;

[TestClass]
public sealed class CompositionRootTests
{
    [TestMethod]
    public void CompositionRootBuildsAValidatedServiceProvider()
    {
        using var serviceProvider = CompositionRoot.CreateServiceProvider();

        Assert.IsNotNull(serviceProvider);
    }
}

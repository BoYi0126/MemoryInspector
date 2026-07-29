using Microsoft.Extensions.DependencyInjection;

namespace MemoryInspector.Plugin;

public interface IMemoryInspectorPlugin
{
    void ConfigureServices(IServiceCollection services);

    ValueTask InitializeAsync(
        IPluginContext context,
        CancellationToken cancellationToken = default);

    IReadOnlyList<IPluginUiContribution> GetUiContributions();

    ValueTask ShutdownAsync(
        CancellationToken cancellationToken = default);
}

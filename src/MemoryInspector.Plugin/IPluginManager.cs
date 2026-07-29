using MemoryInspector.Common;

namespace MemoryInspector.Plugin;

public interface IPluginManager
{
    string PluginsDirectory { get; }

    PluginManagerSnapshot CurrentSnapshot { get; }

    Task<Result<PluginManagerSnapshot>> InitializeAsync(
        CancellationToken cancellationToken = default);

    Task<Result<PluginManagerSnapshot>> RefreshAsync(
        CancellationToken cancellationToken = default);

    Task<Result<PluginManagerSnapshot>> EnableAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    Task<Result<PluginManagerSnapshot>> DisableAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    IReadOnlyList<IPluginUiContribution> GetUiContributions();

    Result OpenPluginsFolder();
}

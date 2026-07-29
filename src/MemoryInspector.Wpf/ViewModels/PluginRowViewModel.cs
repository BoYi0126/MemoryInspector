using MemoryInspector.Plugin;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class PluginRowViewModel(
    PluginDescriptor plugin)
{
    public PluginDescriptor Plugin { get; } =
        plugin ?? throw new ArgumentNullException(nameof(plugin));

    public string Id => Plugin.Id;

    public string Name => Plugin.Name;

    public string VersionDisplay => Plugin.Version;

    public string CapabilitiesDisplay => string.Join(
        ", ",
        Plugin.Capabilities);

    public string StateDisplay => Plugin.State.ToString();

    public string EnabledDisplay =>
        Plugin.IsEnabled ? "Enabled" : "Disabled";

    public string ErrorDisplay =>
        Plugin.ErrorMessage ?? string.Empty;
}

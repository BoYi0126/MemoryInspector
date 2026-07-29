namespace MemoryInspector.Plugin;

public interface IPluginContext
{
    string PluginId { get; }

    string PluginDirectory { get; }

    Version ApiVersion { get; }

    Version HostVersion { get; }

    IServiceProvider Services { get; }

    IPluginLogger Logger { get; }
}

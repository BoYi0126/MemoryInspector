namespace MemoryInspector.Plugin;

public static class PluginApiVersion
{
    public static Version Current { get; } = new(1, 0, 0);

    public static Version HostVersion { get; } = new(1, 0, 0);
}

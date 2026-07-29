namespace MemoryInspector.Plugin;

public sealed record PluginUiResult(
    string Summary,
    string? Details = null);

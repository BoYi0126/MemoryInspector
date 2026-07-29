namespace MemoryInspector.Plugin;

public sealed record PluginDescriptor(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<PluginKind> Capabilities,
    PluginLoadState State,
    bool IsEnabled,
    bool IsLoaded,
    int UiContributionCount,
    string Directory,
    string? Description = null,
    string? Author = null,
    string? ErrorMessage = null);

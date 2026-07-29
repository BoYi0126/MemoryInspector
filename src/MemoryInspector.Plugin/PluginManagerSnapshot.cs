namespace MemoryInspector.Plugin;

public sealed record PluginManagerSnapshot(
    IReadOnlyList<PluginDescriptor> Plugins,
    int LoadedCount,
    int DisabledCount,
    int FailedCount,
    int IncompatibleCount,
    int UiContributionCount);

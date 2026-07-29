using MemoryInspector.Common;

namespace MemoryInspector.Plugin;

public interface IPluginUiContribution
{
    string Id { get; }

    string Title { get; }

    string Description { get; }

    PluginKind Kind { get; }

    ValueTask<Result<PluginUiResult>> ExecuteAsync(
        CancellationToken cancellationToken = default);
}

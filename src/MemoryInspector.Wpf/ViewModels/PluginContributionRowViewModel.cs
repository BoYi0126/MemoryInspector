using MemoryInspector.Plugin;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class PluginContributionRowViewModel(
    IPluginUiContribution contribution)
{
    public IPluginUiContribution Contribution { get; } =
        contribution ??
        throw new ArgumentNullException(nameof(contribution));

    public string Title => Contribution.Title;

    public string Description => Contribution.Description;

    public string KindDisplay => Contribution.Kind.ToString();
}

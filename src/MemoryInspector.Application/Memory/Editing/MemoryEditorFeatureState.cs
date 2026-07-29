namespace MemoryInspector.Application.Memory.Editing;

public sealed record MemoryEditorFeatureState(
    MemoryEditorSettings Settings)
{
    public const string Purpose =
        "Single-value memory editing for debugging and validation.";
    public const string RiskWarning =
        "Writing an incorrect address or value can destabilize or " +
        "terminate the target process.";
    public const string AuthorizedUseStatement =
        "Use only with software you developed, test targets, or " +
        "processes you are explicitly authorized to modify.";

    public bool IsEnabled => Settings.Enabled;

    public DateTimeOffset? EnabledAt => Settings.EnabledAt;
}

namespace MemoryInspector.Application.Memory.Editing;

public sealed record MemoryEditorEnablementAcknowledgement(
    bool AcknowledgesRisk,
    bool ConfirmsAuthorizedTargetsOnly);

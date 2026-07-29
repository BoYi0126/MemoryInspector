namespace MemoryInspector.Wpf.Services;

public interface IMemoryEditorFileDialogService
{
    string? SelectAuditExportFile(string suggestedFileName);
}

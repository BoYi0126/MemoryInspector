using Microsoft.Win32;

namespace MemoryInspector.Wpf.Services;

public sealed class WpfMemoryEditorFileDialogService :
    IMemoryEditorFileDialogService
{
    public string? SelectAuditExportFile(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Memory Editor Audit Summary",
            Filter =
                "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = suggestedFileName,
        };
        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}

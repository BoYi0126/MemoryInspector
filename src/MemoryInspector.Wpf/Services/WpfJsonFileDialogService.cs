using Microsoft.Win32;

namespace MemoryInspector.Wpf.Services;

public sealed class WpfJsonFileDialogService :
    IJsonFileDialogService
{
    private const string JsonFilter =
        "JSON files (*.json)|*.json|All files (*.*)|*.*";

    public string? SelectImportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Saved Addresses",
            Filter = JsonFilter,
            DefaultExt = ".json",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public string? SelectExportFile(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Saved Addresses",
            Filter = JsonFilter,
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = suggestedFileName,
        };
        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}

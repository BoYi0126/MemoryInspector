using Microsoft.Win32;

namespace MemoryInspector.Wpf.Services;

public sealed class WpfSnapshotCompareFileDialogService :
    ISnapshotCompareFileDialogService
{
    public string? SelectComparisonExportFile(
        string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Snapshot Comparison",
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

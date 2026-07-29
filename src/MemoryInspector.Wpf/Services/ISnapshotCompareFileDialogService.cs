namespace MemoryInspector.Wpf.Services;

public interface ISnapshotCompareFileDialogService
{
    string? SelectComparisonExportFile(string suggestedFileName);
}

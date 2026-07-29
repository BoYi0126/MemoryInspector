namespace MemoryInspector.Wpf.Services;

public interface IJsonFileDialogService
{
    string? SelectImportFile();

    string? SelectExportFile(string suggestedFileName);
}

using MemoryInspector.Wpf.Mvvm;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private int _selectedWorkspaceIndex;

    public MainWindowViewModel(
        ProcessExplorerViewModel processExplorer,
        MemoryRegionViewerViewModel memoryRegions,
        ProcessDetailsViewerViewModel processDetails,
        HexViewerViewModel hexViewer,
        SnapshotCompareViewModel snapshotCompare,
        ResultGridViewModel results,
        WatchWindowViewModel watch,
        SavedAddressWindowViewModel savedAddresses,
        MemoryEditorViewModel memoryEditor,
        TemporaryManagerViewModel temporaryManager,
        PluginManagerViewModel pluginManager)
    {
        ProcessExplorer = processExplorer ??
            throw new ArgumentNullException(nameof(processExplorer));
        MemoryRegions = memoryRegions ??
            throw new ArgumentNullException(nameof(memoryRegions));
        ProcessDetails = processDetails ??
            throw new ArgumentNullException(nameof(processDetails));
        HexViewer = hexViewer ??
            throw new ArgumentNullException(nameof(hexViewer));
        SnapshotCompare = snapshotCompare ??
            throw new ArgumentNullException(nameof(snapshotCompare));
        Results = results ??
            throw new ArgumentNullException(nameof(results));
        Watch = watch ??
            throw new ArgumentNullException(nameof(watch));
        SavedAddresses = savedAddresses ??
            throw new ArgumentNullException(nameof(savedAddresses));
        MemoryEditor = memoryEditor ??
            throw new ArgumentNullException(nameof(memoryEditor));
        TemporaryManager = temporaryManager ??
            throw new ArgumentNullException(
                nameof(temporaryManager));
        PluginManager = pluginManager ??
            throw new ArgumentNullException(nameof(pluginManager));
        Results.AddToWatchRequested +=
            (_, eventArgs) =>
                Watch.AddFromResult(eventArgs.Row);
        Results.SaveAddressRequested +=
            async (_, eventArgs) =>
                await SavedAddresses.AddFromResultAsync(
                    eventArgs.Row);
        Results.EditValueRequested += OnEditValueRequested;
        Results.OpenHexRequested += OnOpenHexRequested;
        MemoryRegions.OpenHexRequested += OnOpenHexRequested;
        Watch.EditValueRequested += OnEditValueRequested;
        SavedAddresses.EditValueRequested += OnEditValueRequested;
        MemoryEditor.WriteCompleted += OnWriteCompleted;
    }

    public ProcessExplorerViewModel ProcessExplorer { get; }

    public MemoryRegionViewerViewModel MemoryRegions { get; }

    public ProcessDetailsViewerViewModel ProcessDetails { get; }

    public HexViewerViewModel HexViewer { get; }

    public SnapshotCompareViewModel SnapshotCompare { get; }

    public ResultGridViewModel Results { get; }

    public WatchWindowViewModel Watch { get; }

    public SavedAddressWindowViewModel SavedAddresses { get; }

    public MemoryEditorViewModel MemoryEditor { get; }

    public TemporaryManagerViewModel TemporaryManager { get; }

    public PluginManagerViewModel PluginManager { get; }

    public int SelectedWorkspaceIndex
    {
        get => _selectedWorkspaceIndex;
        set => SetProperty(ref _selectedWorkspaceIndex, value);
    }

    private void OnEditValueRequested(
        object? sender,
        MemoryEditRequestedEventArgs eventArgs)
    {
        SelectedWorkspaceIndex = 5;
        _ = MemoryEditor.OpenAsync(
            eventArgs.Address,
            eventArgs.ValueType,
            eventArgs.Source);
    }

    private void OnWriteCompleted(
        object? sender,
        MemoryWriteCompletedEventArgs eventArgs)
    {
        Results.ApplyMemoryWriteResult(
            eventArgs.Request,
            eventArgs.Result);
        _ = Watch.RefreshAsync();
        _ = SavedAddresses.ValidateAsync();
    }

    private async void OnOpenHexRequested(
        object? sender,
        HexViewerRequestedEventArgs eventArgs)
    {
        SelectedWorkspaceIndex = 9;

        if (eventArgs.Region is not null)
        {
            await HexViewer.OpenRegionAsync(
                eventArgs.Region,
                eventArgs.Address);
            return;
        }

        await HexViewer.OpenAddressAsync(eventArgs.Address);
    }
}

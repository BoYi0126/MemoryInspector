using System.Collections.ObjectModel;
using MemoryInspector.Common;
using MemoryInspector.Plugin;
using MemoryInspector.Wpf.Mvvm;
using MemoryInspector.Wpf.Services;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class PluginManagerViewModel : ObservableObject
{
    private readonly IPluginManager _pluginManager;
    private readonly IUserConfirmationService _confirmationService;
    private PluginRowViewModel? _selectedPlugin;
    private PluginContributionRowViewModel?
        _selectedContribution;
    private bool _isBusy;
    private int _loadedCount;
    private int _disabledCount;
    private int _failedCount;
    private int _incompatibleCount;
    private string _statusMessage =
        "Plugin Manager has not been initialized.";
    private string? _errorMessage;
    private string? _contributionOutput;

    public PluginManagerViewModel(
        IPluginManager pluginManager,
        IUserConfirmationService confirmationService)
    {
        _pluginManager = pluginManager ??
            throw new ArgumentNullException(
                nameof(pluginManager));
        _confirmationService = confirmationService ??
            throw new ArgumentNullException(
                nameof(confirmationService));
        Plugins = [];
        Contributions = [];
        RefreshCommand = new AsyncRelayCommand(
            RefreshAsync,
            () => !IsBusy);
        ToggleEnabledCommand = new AsyncRelayCommand(
            ToggleSelectedAsync,
            () => !IsBusy && SelectedPlugin is not null);
        ExecuteContributionCommand = new AsyncRelayCommand(
            ExecuteSelectedContributionAsync,
            () => !IsBusy &&
                  SelectedContribution is not null);
        OpenPluginsFolderCommand = new RelayCommand(
            OpenPluginsFolder);
    }

    public ObservableCollection<PluginRowViewModel> Plugins
    {
        get;
    }

    public ObservableCollection<PluginContributionRowViewModel>
        Contributions { get; }

    public PluginRowViewModel? SelectedPlugin
    {
        get => _selectedPlugin;
        set
        {
            if (SetProperty(ref _selectedPlugin, value))
            {
                ToggleEnabledCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ToggleEnabledLabel));
            }
        }
    }

    public PluginContributionRowViewModel? SelectedContribution
    {
        get => _selectedContribution;
        set
        {
            if (SetProperty(ref _selectedContribution, value))
            {
                ExecuteContributionCommand
                    .NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    public int LoadedCount
    {
        get => _loadedCount;
        private set => SetProperty(ref _loadedCount, value);
    }

    public int DisabledCount
    {
        get => _disabledCount;
        private set => SetProperty(ref _disabledCount, value);
    }

    public int FailedCount
    {
        get => _failedCount;
        private set => SetProperty(ref _failedCount, value);
    }

    public int IncompatibleCount
    {
        get => _incompatibleCount;
        private set => SetProperty(
            ref _incompatibleCount,
            value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string? ContributionOutput
    {
        get => _contributionOutput;
        private set => SetProperty(
            ref _contributionOutput,
            value);
    }

    public string PluginsDirectory =>
        _pluginManager.PluginsDirectory;

    public string ToggleEnabledLabel =>
        SelectedPlugin?.Plugin.IsEnabled == true
            ? "Disable Selected"
            : "Enable Selected";

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ToggleEnabledCommand { get; }

    public AsyncRelayCommand ExecuteContributionCommand { get; }

    public RelayCommand OpenPluginsFolderCommand { get; }

    public Task InitializeAsync()
    {
        ApplySnapshot(_pluginManager.CurrentSnapshot);
        StatusMessage =
            $"Plugin Manager ready: {LoadedCount:N0} loaded.";
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        await RunManagerOperationAsync(
            () => _pluginManager.RefreshAsync(),
            "Discovering and loading plugins…");
    }

    private async Task ToggleSelectedAsync()
    {
        var selected = SelectedPlugin;

        if (selected is null)
        {
            return;
        }

        if (selected.Plugin.IsEnabled &&
            !_confirmationService.Confirm(
                "Disable plugin",
                $"Disable '{selected.Name}' and dispose its " +
                "isolated services?"))
        {
            return;
        }

        await RunManagerOperationAsync(
            () => selected.Plugin.IsEnabled
                ? _pluginManager.DisableAsync(selected.Id)
                : _pluginManager.EnableAsync(selected.Id),
            selected.Plugin.IsEnabled
                ? "Disabling plugin…"
                : "Enabling plugin…");
    }

    private async Task ExecuteSelectedContributionAsync()
    {
        var selected = SelectedContribution;

        if (selected is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        ContributionOutput = null;
        StatusMessage =
            $"Running {selected.Contribution.Title}…";

        try
        {
            var result = await selected.Contribution
                .ExecuteAsync();

            if (result.IsFailure)
            {
                ErrorMessage =
                    result.Error.ToDisplayMessage();
                StatusMessage = ErrorMessage;
                return;
            }

            ContributionOutput = string.IsNullOrWhiteSpace(
                    result.Value.Details)
                ? result.Value.Summary
                : $"{result.Value.Summary}{Environment.NewLine}" +
                  result.Value.Details;
            StatusMessage =
                $"{selected.Contribution.Title} completed.";
        }
        catch (Exception exception)
        {
            ErrorMessage =
                $"Plugin contribution failed: {exception.Message}";
            StatusMessage = ErrorMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunManagerOperationAsync(
        Func<Task<Result<PluginManagerSnapshot>>> operation,
        string progressMessage)
    {
        IsBusy = true;
        ErrorMessage = null;
        ContributionOutput = null;
        StatusMessage = progressMessage;

        try
        {
            var result = await operation();

            if (result.IsFailure)
            {
                ErrorMessage =
                    result.Error.ToDisplayMessage();
                StatusMessage = ErrorMessage;
                return;
            }

            ApplySnapshot(result.Value);
            StatusMessage =
                $"Plugin Manager updated: {LoadedCount:N0} loaded, " +
                $"{FailedCount + IncompatibleCount:N0} unavailable.";
        }
        catch (Exception exception)
        {
            ErrorMessage =
                $"Plugin Manager operation failed: " +
                exception.Message;
            StatusMessage = ErrorMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySnapshot(PluginManagerSnapshot snapshot)
    {
        var selectedId = SelectedPlugin?.Id;
        Plugins.Clear();

        foreach (var plugin in snapshot.Plugins)
        {
            Plugins.Add(new PluginRowViewModel(plugin));
        }

        SelectedPlugin = Plugins.FirstOrDefault(plugin =>
                plugin.Id.Equals(
                    selectedId,
                    StringComparison.OrdinalIgnoreCase)) ??
            Plugins.FirstOrDefault();
        Contributions.Clear();

        foreach (var contribution in
                 _pluginManager.GetUiContributions())
        {
            Contributions.Add(
                new PluginContributionRowViewModel(
                    contribution));
        }

        SelectedContribution = Contributions.FirstOrDefault();
        LoadedCount = snapshot.LoadedCount;
        DisabledCount = snapshot.DisabledCount;
        FailedCount = snapshot.FailedCount;
        IncompatibleCount = snapshot.IncompatibleCount;
        NotifyCommands();
    }

    private void OpenPluginsFolder()
    {
        var result = _pluginManager.OpenPluginsFolder();
        ErrorMessage = result.IsFailure
            ? result.Error.ToDisplayMessage()
            : null;
        StatusMessage = result.IsSuccess
            ? "Opened the plugin folder."
            : ErrorMessage!;
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ToggleEnabledCommand.NotifyCanExecuteChanged();
        ExecuteContributionCommand.NotifyCanExecuteChanged();
    }
}

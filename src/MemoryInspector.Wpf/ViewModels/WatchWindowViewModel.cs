using System.Globalization;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Watch;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Wpf.Mvvm;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class WatchWindowViewModel :
    ObservableObject,
    IDisposable
{
    private readonly object _loopSync = new();
    private readonly IWatchService _watchService;
    private readonly IAppLogger _logger;
    private readonly SynchronizationContext? _synchronizationContext;
    private IReadOnlyList<WatchEntryRowViewModel> _entries = [];
    private WatchEntryRowViewModel? _selectedEntry;
    private CancellationTokenSource? _loopCancellation;
    private string _addressText = string.Empty;
    private ScanValueType _selectedValueType =
        ScanValueType.Int32;
    private WatchRefreshIntervalOption _selectedRefreshInterval =
        WatchRefreshIntervalOption.Defaults[1];
    private string _customIntervalText = "500";
    private bool _isPaused;
    private bool _isBusy;
    private string _statusMessage =
        "Add an address to begin watching values.";
    private string? _errorMessage;
    private bool _disposed;

    public WatchWindowViewModel(
        IWatchService watchService,
        IAppLogger logger)
    {
        _watchService = Guard.NotNull(watchService);
        _logger = Guard.NotNull(logger);
        _synchronizationContext = SynchronizationContext.Current;
        AddCommand = new RelayCommand(AddFromInput);
        RemoveCommand = new RelayCommand(
            RemoveSelected,
            () => SelectedEntry is not null);
        ChangeTypeCommand = new RelayCommand(
            ChangeSelectedType,
            () => SelectedEntry is not null);
        PauseCommand = new RelayCommand(
            Pause,
            () => !IsPaused && Entries.Count > 0);
        ResumeCommand = new AsyncRelayCommand(
            ResumeAsync,
            () => IsPaused && Entries.Count > 0);
        RefreshCommand = new AsyncRelayCommand(
            () => RefreshAsync(),
            () => _watchService.CanRefresh);
        EditValueCommand = new RelayCommand(
            RequestEditValue,
            () => SelectedEntry is not null);
        watchService.EntriesChanged += OnEntriesChanged;
        ApplyEntries(
            watchService.Entries,
            watchService.IsPaused,
            watchService.CanRefresh);
    }

    public IReadOnlyList<ScanValueType> ValueTypes { get; } =
        Enum.GetValues<ScanValueType>();

    public event EventHandler<MemoryEditRequestedEventArgs>?
        EditValueRequested;

    public IReadOnlyList<WatchRefreshIntervalOption>
        RefreshIntervals =>
        WatchRefreshIntervalOption.Defaults;

    public IReadOnlyList<WatchEntryRowViewModel> Entries
    {
        get => _entries;
        private set => SetProperty(ref _entries, value);
    }

    public WatchEntryRowViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                if (value is not null)
                {
                    SelectedValueType = value.ValueType;
                }

                NotifyCommands();
            }
        }
    }

    public string AddressText
    {
        get => _addressText;
        set => SetProperty(
            ref _addressText,
            value ?? string.Empty);
    }

    public ScanValueType SelectedValueType
    {
        get => _selectedValueType;
        set => SetProperty(ref _selectedValueType, value);
    }

    public WatchRefreshIntervalOption SelectedRefreshInterval
    {
        get => _selectedRefreshInterval;
        set
        {
            if (SetProperty(
                ref _selectedRefreshInterval,
                value ?? RefreshIntervals[1]))
            {
                OnPropertyChanged(
                    nameof(IsCustomIntervalSelected));
                RestartLoop();
            }
        }
    }

    public string CustomIntervalText
    {
        get => _customIntervalText;
        set
        {
            if (SetProperty(
                ref _customIntervalText,
                value ?? string.Empty) &&
                IsCustomIntervalSelected)
            {
                RestartLoop();
            }
        }
    }

    public bool IsCustomIntervalSelected =>
        SelectedRefreshInterval.IsCustom;

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
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

    public string EntryCountDisplay =>
        $"{Entries.Count:N0} watched addresses";

    public RelayCommand AddCommand { get; }

    public RelayCommand RemoveCommand { get; }

    public RelayCommand ChangeTypeCommand { get; }

    public RelayCommand PauseCommand { get; }

    public AsyncRelayCommand ResumeCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand EditValueCommand { get; }

    public void Initialize(AppSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Guard.NotNull(settings);
        var match = RefreshIntervals.FirstOrDefault(option =>
            option.Milliseconds ==
            settings.WatchRefreshIntervalMilliseconds);

        if (match is not null)
        {
            SelectedRefreshInterval = match;
        }
        else
        {
            CustomIntervalText =
                settings.WatchRefreshIntervalMilliseconds
                    .ToString(CultureInfo.InvariantCulture);
            SelectedRefreshInterval =
                RefreshIntervals.Single(option =>
                    option.IsCustom);
        }

        StartLoopIfNeeded();
    }

    public Result<WatchEntry> AddFromResult(
        ResultGridRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var result = _watchService.Add(
            row.Address,
            row.ValueType);
        ApplyActionResult(
            result.IsSuccess
                ? $"Watching {row.AddressDisplay} as " +
                  $"{row.ValueType}."
                : null,
            result);
        return result;
    }

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await _watchService.RefreshAsync(
                cancellationToken);

            if (result.IsFailure)
            {
                if (result.Error.Code != ErrorCode.Cancelled)
                {
                    ErrorMessage = result.Error.ToDisplayMessage();
                    StatusMessage = ErrorMessage;
                    _ = _logger.Log(
                        AppLogLevel.Warning,
                        ErrorMessage,
                        result.Error.Exception);
                }

                return;
            }

            StatusMessage =
                $"Updated {result.Value.AvailableCount:N0} of " +
                $"{result.Value.AttemptedCount:N0} watched addresses.";
            ErrorMessage = result.Value.IsPartial
                ? $"{result.Value.UnreadableCount:N0} address(es) " +
                  "could not be read."
                : null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _watchService.EntriesChanged -= OnEntriesChanged;
        StopLoop();
        _disposed = true;
    }

    private void AddFromInput()
    {
        if (!TryParseAddress(AddressText, out var address))
        {
            ErrorMessage =
                "Address must be an x64 hexadecimal value.";
            return;
        }

        var result = _watchService.Add(
            address,
            SelectedValueType);
        ApplyActionResult(
            result.IsSuccess
                ? $"Watching 0x{address:X16} as " +
                  $"{SelectedValueType}."
                : null,
            result);

        if (result.IsSuccess)
        {
            AddressText = string.Empty;
        }
    }

    private void RemoveSelected()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var address = SelectedEntry.AddressDisplay;
        var result = _watchService.Remove(
            SelectedEntry.Key);
        ApplyActionResult(
            result.IsSuccess
                ? $"Removed {address} from Watch."
                : null,
            result);
    }

    private void ChangeSelectedType()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var result = _watchService.ChangeType(
            SelectedEntry.Key,
            SelectedValueType);
        ApplyActionResult(
            result.IsSuccess
                ? $"Changed {SelectedEntry.AddressDisplay} " +
                  $"to {SelectedValueType}."
                : null,
            result);
    }

    private void RequestEditValue()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        EditValueRequested?.Invoke(
            this,
            new MemoryEditRequestedEventArgs(
                SelectedEntry.Address,
                SelectedEntry.ValueType,
                MemoryWriteSource.WatchWindow));
        StatusMessage =
            $"{SelectedEntry.AddressDisplay} was sent to Memory Editor.";
    }

    private void Pause()
    {
        var result = _watchService.SetPaused(true);
        ApplyActionResult(
            result.IsSuccess ? "Watch refresh is paused." : null,
            result);
        StopLoop();
    }

    private async Task ResumeAsync()
    {
        var result = _watchService.SetPaused(false);
        ApplyActionResult(
            result.IsSuccess ? "Watch refresh resumed." : null,
            result);

        if (result.IsSuccess)
        {
            StartLoopIfNeeded();
            await RefreshAsync();
        }
    }

    private void ApplyEntries(
        IReadOnlyList<WatchEntry> entries,
        bool isPaused,
        bool canRefresh)
    {
        var selectedKey = SelectedEntry?.Key;
        Entries = Array.AsReadOnly(
            entries
                .Select(entry =>
                    new WatchEntryRowViewModel(entry))
                .ToArray());
        SelectedEntry = selectedKey.HasValue
            ? Entries.FirstOrDefault(entry =>
                entry.Key == selectedKey.Value)
            : null;
        IsPaused = isPaused;
        OnPropertyChanged(nameof(EntryCountDisplay));
        NotifyCommands();

        if (canRefresh)
        {
            StartLoopIfNeeded();
        }
        else
        {
            StopLoop();
        }
    }

    private void OnEntriesChanged(
        object? sender,
        WatchEntriesChangedEventArgs eventArgs)
    {
        if (_synchronizationContext is not null &&
            SynchronizationContext.Current !=
            _synchronizationContext)
        {
            _synchronizationContext.Post(
                _ => ApplyEntries(
                    eventArgs.Entries,
                    eventArgs.IsPaused,
                    eventArgs.CanRefresh),
                null);
            return;
        }

        ApplyEntries(
            eventArgs.Entries,
            eventArgs.IsPaused,
            eventArgs.CanRefresh);
    }

    private void RestartLoop()
    {
        StopLoop();
        StartLoopIfNeeded();
    }

    private void StartLoopIfNeeded()
    {
        if (!_watchService.CanRefresh ||
            !TryGetRefreshInterval(out var interval))
        {
            return;
        }

        lock (_loopSync)
        {
            if (_loopCancellation is not null)
            {
                return;
            }

            _loopCancellation =
                new CancellationTokenSource();
            _ = RunRefreshLoopAsync(
                interval,
                _loopCancellation.Token);
        }
    }

    private void StopLoop()
    {
        lock (_loopSync)
        {
            _loopCancellation?.Cancel();
            _loopCancellation?.Dispose();
            _loopCancellation = null;
        }
    }

    private async Task RunRefreshLoopAsync(
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken);
                await RefreshAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool TryGetRefreshInterval(
        out TimeSpan interval)
    {
        var milliseconds =
            SelectedRefreshInterval.Milliseconds;

        if (!milliseconds.HasValue &&
            (!int.TryParse(
                CustomIntervalText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var custom) ||
             custom < MinimumCustomIntervalMilliseconds ||
             custom > MaximumCustomIntervalMilliseconds))
        {
            ErrorMessage =
                $"Custom interval must be between " +
                $"{MinimumCustomIntervalMilliseconds:N0} and " +
                $"{MaximumCustomIntervalMilliseconds:N0} ms.";
            interval = default;
            return false;
        }

        interval = TimeSpan.FromMilliseconds(
            milliseconds ?? int.Parse(
                CustomIntervalText,
                CultureInfo.InvariantCulture));
        ErrorMessage = null;
        return true;
    }

    private void ApplyActionResult<T>(
        string? successMessage,
        Result<T> result)
    {
        if (result.IsSuccess)
        {
            StatusMessage = successMessage ??
                "Watch action completed.";
            ErrorMessage = null;
        }
        else
        {
            ErrorMessage = result.Error.ToDisplayMessage();
            StatusMessage = ErrorMessage;
        }
    }

    private void ApplyActionResult(
        string? successMessage,
        Result result)
    {
        if (result.IsSuccess)
        {
            StatusMessage = successMessage ??
                "Watch action completed.";
            ErrorMessage = null;
        }
        else
        {
            ErrorMessage = result.Error.ToDisplayMessage();
            StatusMessage = ErrorMessage;
        }
    }

    private void NotifyCommands()
    {
        RemoveCommand.NotifyCanExecuteChanged();
        ChangeTypeCommand.NotifyCanExecuteChanged();
        EditValueCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private static bool TryParseAddress(
        string text,
        out ulong address)
    {
        address = 0;
        var trimmed = (text ?? string.Empty).Trim();

        if (trimmed.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return trimmed.Length > 0 &&
               ulong.TryParse(
                   trimmed,
                   NumberStyles.AllowHexSpecifier,
                   CultureInfo.InvariantCulture,
                   out address);
    }

    private const int MinimumCustomIntervalMilliseconds =
        AppSettings.MinimumWatchRefreshIntervalMilliseconds;
    private const int MaximumCustomIntervalMilliseconds =
        AppSettings.MaximumWatchRefreshIntervalMilliseconds;
}

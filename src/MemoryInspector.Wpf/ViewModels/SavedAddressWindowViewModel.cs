using System.Globalization;
using System.IO;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.SavedAddresses;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Wpf.Mvvm;
using MemoryInspector.Wpf.Services;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class SavedAddressWindowViewModel :
    ObservableObject,
    IDisposable
{
    private readonly ISavedAddressService _savedAddressService;
    private readonly IMonitoringSessionService
        _monitoringSessionService;
    private readonly IMemoryReaderService _memoryReaderService;
    private readonly IUserConfirmationService
        _confirmationService;
    private readonly IJsonFileDialogService _fileDialogService;
    private readonly IAppLogger _logger;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly Dictionary<
        string,
        (
            SavedAddressReadStatus Status,
            string? Message,
            byte[]? Value)>
        _validation = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _validationCancellation;
    private IReadOnlyList<SavedAddressRowViewModel> _entries = [];
    private SavedAddressRowViewModel? _selectedEntry;
    private string _keyText = string.Empty;
    private string _addressText = string.Empty;
    private ScanValueType _selectedValueType =
        ScanValueType.Int32;
    private string _descriptionText = string.Empty;
    private string _targetDisplay = "No saved target";
    private string _statusMessage =
        "Saved addresses have not been loaded.";
    private string? _errorMessage;
    private bool _isBusy;
    private bool _initialized;
    private bool _disposed;

    public SavedAddressWindowViewModel(
        ISavedAddressService savedAddressService,
        IMonitoringSessionService monitoringSessionService,
        IMemoryReaderService memoryReaderService,
        IUserConfirmationService confirmationService,
        IJsonFileDialogService fileDialogService,
        IAppLogger logger)
    {
        _savedAddressService =
            Guard.NotNull(savedAddressService);
        _monitoringSessionService =
            Guard.NotNull(monitoringSessionService);
        _memoryReaderService =
            Guard.NotNull(memoryReaderService);
        _confirmationService =
            Guard.NotNull(confirmationService);
        _fileDialogService =
            Guard.NotNull(fileDialogService);
        _logger = Guard.NotNull(logger);
        _synchronizationContext =
            SynchronizationContext.Current;
        AddCommand = new AsyncRelayCommand(
            AddFromInputAsync,
            () => _initialized && !IsBusy);
        RenameCommand = new AsyncRelayCommand(
            RenameSelectedAsync,
            () => SelectedEntry is not null && !IsBusy);
        UpdateCommand = new AsyncRelayCommand(
            UpdateSelectedAsync,
            () => SelectedEntry is not null && !IsBusy);
        DeleteCommand = new AsyncRelayCommand(
            DeleteSelectedAsync,
            () => SelectedEntry is not null && !IsBusy);
        ImportCommand = new AsyncRelayCommand(
            ImportAsync,
            () => !IsBusy);
        ExportCommand = new AsyncRelayCommand(
            ExportAsync,
            () => _initialized && !IsBusy);
        EditValueCommand = new RelayCommand(
            RequestEditValue,
            () => SelectedEntry is not null &&
                  !SelectedEntry.IsUnreadable);
        savedAddressService.CatalogChanged += OnCatalogChanged;
        monitoringSessionService.SessionChanged +=
            OnSessionChanged;
        ApplyCatalog(savedAddressService.Catalog);
    }

    public IReadOnlyList<ScanValueType> ValueTypes { get; } =
        Enum.GetValues<ScanValueType>();

    public event EventHandler<MemoryEditRequestedEventArgs>?
        EditValueRequested;

    public IReadOnlyList<SavedAddressRowViewModel> Entries
    {
        get => _entries;
        private set => SetProperty(ref _entries, value);
    }

    public SavedAddressRowViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
            {
                return;
            }

            if (value is not null)
            {
                KeyText = value.Key;
                AddressText = value.AddressDisplay;
                SelectedValueType = value.ValueType;
                DescriptionText = value.Description;
            }

            NotifyCommands();
        }
    }

    public string KeyText
    {
        get => _keyText;
        set => SetProperty(ref _keyText, value ?? string.Empty);
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

    public string DescriptionText
    {
        get => _descriptionText;
        set => SetProperty(
            ref _descriptionText,
            value ?? string.Empty);
    }

    public string TargetDisplay
    {
        get => _targetDisplay;
        private set => SetProperty(ref _targetDisplay, value);
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

    public string EntryCountDisplay =>
        $"{Entries.Count:N0} saved addresses";

    public AsyncRelayCommand AddCommand { get; }

    public AsyncRelayCommand RenameCommand { get; }

    public AsyncRelayCommand UpdateCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public RelayCommand EditValueCommand { get; }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result =
                await _savedAddressService.InitializeAsync(
                    cancellationToken);
            _initialized = result.IsSuccess;

            if (result.IsSuccess)
            {
                ApplyCatalog(result.Value);
                StatusMessage =
                    $"Loaded {result.Value.Entries.Count:N0} " +
                    "saved addresses.";
                _ = await ValidateAsync(cancellationToken);
            }
            else
            {
                ApplyFailure(
                    "Saved addresses could not be loaded.",
                    result.Error);
            }
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    public async Task<Result<SavedAddressEntry>> AddFromResultAsync(
        ResultGridRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var key = $"Address_{row.Address:X16}";
        var result = await AddAsync(
            key,
            row.Address,
            row.ValueType,
            "Saved from Results",
            cancellationToken);

        if (result.IsSuccess)
        {
            StatusMessage =
                $"Saved {row.AddressDisplay} as '{result.Value.Key}'.";
        }

        return result;
    }

    public async Task<Result> ValidateAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _validationCancellation?.Cancel();
        _validationCancellation?.Dispose();
        _validationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var token = _validationCancellation.Token;
        var catalog = _savedAddressService.Catalog;

        if (catalog.Entries.Count == 0)
        {
            ApplyValidation(
                new Dictionary<
                    string,
                    (
                        SavedAddressReadStatus Status,
                        string? Message,
                        byte[]? Value)>(
                        StringComparer.OrdinalIgnoreCase));
            return Result.Success();
        }

        var session = GetConnectedSession();

        if (session is null)
        {
            ApplyValidation(catalog.Entries.ToDictionary(
                entry => entry.Key,
                _ => (
                    SavedAddressReadStatus.TargetUnavailable,
                    (string?)"No monitoring target is connected.",
                    (byte[]?)null),
                StringComparer.OrdinalIgnoreCase));
            return Result.Success();
        }

        if (catalog.Target is null ||
            catalog.Target.Architecture !=
                session.Identity.Architecture ||
            !string.Equals(
                catalog.Target.ProcessName,
                session.Identity.ProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            ApplyValidation(catalog.Entries.ToDictionary(
                entry => entry.Key,
                _ => (
                    SavedAddressReadStatus.TargetMismatch,
                    (string?)(
                        "The connected target does not match " +
                        "the saved catalog."),
                    (byte[]?)null),
                StringComparer.OrdinalIgnoreCase));
            return Result.Success();
        }

        var requests = catalog.Entries
            .Select(entry =>
                new MemoryReadRequest(
                    entry.Address,
                    ScanValueTypeInfo.GetSize(entry.ValueType)))
            .ToArray();
        var read = await _memoryReaderService.ReadBatchAsync(
            requests,
            cancellationToken: token);

        if (read.IsFailure)
        {
            if (read.Error.Code == ErrorCode.Cancelled)
            {
                return Result.Failure(read.Error);
            }

            ApplyValidation(catalog.Entries.ToDictionary(
                entry => entry.Key,
                _ => (
                    SavedAddressReadStatus.Unreadable,
                    (string?)read.Error.ToDisplayMessage(),
                    (byte[]?)null),
                StringComparer.OrdinalIgnoreCase));
            return Result.Failure(read.Error);
        }

        if (read.Value.Items.Count != catalog.Entries.Count)
        {
            var error = new Error(
                ErrorCode.InvalidState,
                "Saved-address validation returned an " +
                "unexpected item count.");
            ApplyValidation(catalog.Entries.ToDictionary(
                entry => entry.Key,
                _ => (
                    SavedAddressReadStatus.Unreadable,
                    (string?)error.Message,
                    (byte[]?)null),
                StringComparer.OrdinalIgnoreCase));
            return Result.Failure(error);
        }

        var validation = new Dictionary<
            string,
            (
                SavedAddressReadStatus Status,
                string? Message,
                byte[]? Value)>(
                StringComparer.OrdinalIgnoreCase);
        var available = 0;

        for (var index = 0;
             index < catalog.Entries.Count;
             index++)
        {
            var entry = catalog.Entries[index];
            var item = read.Value.Items[index];
            var isAvailable =
                item.Result.IsSuccess &&
                item.Result.Value.IsComplete &&
                item.Result.Value.Data.Length ==
                    requests[index].Length;

            if (isAvailable)
            {
                validation[entry.Key] = (
                    SavedAddressReadStatus.Available,
                    null,
                    item.Result.Value.Data.ToArray());
                available++;
            }
            else
            {
                validation[entry.Key] = (
                    SavedAddressReadStatus.Unreadable,
                    item.Result.IsFailure
                        ? item.Result.Error.ToDisplayMessage()
                        : "A complete value could not be read.",
                    null);
            }
        }

        if (token.IsCancellationRequested)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Saved-address validation was cancelled."));
        }

        ApplyValidation(validation);
        StatusMessage =
            $"Validated {available:N0} of " +
            $"{catalog.Entries.Count:N0} saved addresses.";
        return Result.Success();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _savedAddressService.CatalogChanged -= OnCatalogChanged;
        _monitoringSessionService.SessionChanged -=
            OnSessionChanged;
        _validationCancellation?.Cancel();
        _validationCancellation?.Dispose();
        _disposed = true;
    }

    private async Task AddFromInputAsync()
    {
        if (!TryParseAddress(AddressText, out var address))
        {
            ErrorMessage =
                "Address must be an x64 hexadecimal value.";
            return;
        }

        _ = await AddAsync(
            KeyText,
            address,
            SelectedValueType,
            DescriptionText);
    }

    private async Task<Result<SavedAddressEntry>> AddAsync(
        string key,
        ulong address,
        ScanValueType valueType,
        string? description,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return Fail<SavedAddressEntry>(
                new Error(
                    ErrorCode.InvalidState,
                    "Saved addresses have not been initialized."));
        }

        var session = GetConnectedSession();

        if (session is null)
        {
            return Fail<SavedAddressEntry>(
                new Error(
                    ErrorCode.InvalidState,
                    "A connected monitoring session is required."));
        }

        var duplicateBehavior = DuplicateKeyBehavior.Reject;

        if (_savedAddressService.Catalog.Entries.Any(entry =>
            string.Equals(
                entry.Key,
                key.Trim(),
                StringComparison.OrdinalIgnoreCase)))
        {
            var overwrite = _confirmationService.Confirm(
                "Duplicate saved-address key",
                $"The key '{key.Trim()}' already exists. " +
                "Replace its address, type, and description?");

            if (!overwrite)
            {
                return Fail<SavedAddressEntry>(
                    new Error(
                        ErrorCode.Cancelled,
                        "The duplicate key was not overwritten."),
                    log: false);
            }

            duplicateBehavior = DuplicateKeyBehavior.Overwrite;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await _savedAddressService.AddAsync(
                new SavedAddressTarget(
                    session.Identity.ProcessName,
                    session.Identity.Architecture),
                key,
                address,
                valueType,
                description,
                duplicateBehavior,
                cancellationToken);

            if (result.IsSuccess)
            {
                StatusMessage =
                    $"Saved 0x{address:X16} as '{result.Value.Key}'.";
                ErrorMessage = null;
            }
            else
            {
                ApplyFailure(
                    "The address could not be saved.",
                    result.Error);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RenameSelectedAsync()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var duplicateBehavior = DuplicateKeyBehavior.Reject;
        var newKey = KeyText.Trim();
        var duplicate = Entries.Any(entry =>
            entry != SelectedEntry &&
            string.Equals(
                entry.Key,
                newKey,
                StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            var overwrite = _confirmationService.Confirm(
                "Duplicate saved-address key",
                $"The key '{newKey}' already exists. Replace it?");

            if (!overwrite)
            {
                return;
            }

            duplicateBehavior = DuplicateKeyBehavior.Overwrite;
        }

        var oldKey = SelectedEntry.Key;
        await RunMutationAsync(
            () => _savedAddressService.RenameAsync(
                oldKey,
                newKey,
                duplicateBehavior),
            result => $"Renamed '{oldKey}' to '{result.Key}'.",
            "The saved address could not be renamed.");
    }

    private async Task UpdateSelectedAsync()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var key = SelectedEntry.Key;
        await RunMutationAsync(
            () => _savedAddressService.UpdateAsync(
                key,
                SelectedValueType,
                DescriptionText),
            result => $"Updated '{result.Key}'.",
            "The saved address could not be updated.");
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var key = SelectedEntry.Key;

        if (!_confirmationService.Confirm(
            "Delete saved address",
            $"Delete '{key}' permanently?"))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result =
                await _savedAddressService.DeleteAsync(key);

            if (result.IsSuccess)
            {
                StatusMessage = $"Deleted '{key}'.";
                ErrorMessage = null;
            }
            else
            {
                ApplyFailure(
                    "The saved address could not be deleted.",
                    result.Error);
            }
        }
        finally
        {
            IsBusy = false;
        }
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
                MemoryWriteSource.SavedAddress));
        StatusMessage =
            $"{SelectedEntry.AddressDisplay} was sent to Memory Editor.";
    }

    private async Task ImportAsync()
    {
        var path = _fileDialogService.SelectImportFile();

        if (path is null)
        {
            return;
        }

        if (Entries.Count > 0 &&
            !_confirmationService.Confirm(
                "Import saved addresses",
                "Import replaces the current saved-address catalog. Continue?"))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result =
                await _savedAddressService.ImportAsync(path);

            if (result.IsSuccess)
            {
                _initialized = true;
                StatusMessage =
                    $"Imported {result.Value.Entries.Count:N0} " +
                    "saved addresses.";
                ErrorMessage = null;
            }
            else
            {
                ApplyFailure(
                    "Saved addresses could not be imported.",
                    result.Error);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAsync()
    {
        var targetName =
            _savedAddressService.Catalog.Target?.ProcessName ??
            "saved-addresses";
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(
            targetName
                .Select(character =>
                    invalid.Contains(character) ? '_' : character)
                .ToArray());
        var path = _fileDialogService.SelectExportFile(
            $"{safeName}-addresses.json");

        if (path is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result =
                await _savedAddressService.ExportAsync(path);

            if (result.IsSuccess)
            {
                StatusMessage =
                    $"Exported {Entries.Count:N0} saved addresses.";
                ErrorMessage = null;
            }
            else
            {
                ApplyFailure(
                    "Saved addresses could not be exported.",
                    result.Error);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunMutationAsync(
        Func<Task<Result<SavedAddressEntry>>> action,
        Func<SavedAddressEntry, string> successMessage,
        string failureMessage)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await action();

            if (result.IsSuccess)
            {
                StatusMessage = successMessage(result.Value);
            }
            else
            {
                ApplyFailure(failureMessage, result.Error);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Result<T> Fail<T>(Error error, bool log = true)
    {
        ApplyFailure(
            "The address could not be saved.",
            error,
            log);
        return Result<T>.Failure(error);
    }

    private void ApplyFailure(
        string context,
        Error error,
        bool log = true)
    {
        ErrorMessage =
            $"{context} {error.ToDisplayMessage()}";
        StatusMessage = ErrorMessage;

        if (log && error.Code != ErrorCode.Cancelled)
        {
            _ = _logger.Log(
                AppLogLevel.Warning,
                ErrorMessage,
                error.Exception);
        }
    }

    private void ApplyCatalog(SavedAddressCatalog catalog)
    {
        var selectedKey = SelectedEntry?.Key;
        Entries = Array.AsReadOnly(
            catalog.Entries
                .OrderBy(
                    entry => entry.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(entry =>
                {
                    var status = _validation.TryGetValue(
                        entry.Key,
                        out var current)
                        ? current
                        : (
                            SavedAddressReadStatus.Unverified,
                            (string?)null,
                            (byte[]?)null);
                    return new SavedAddressRowViewModel(
                        entry,
                        status.Item1,
                        status.Item2,
                        status.Item3);
                })
                .ToArray());
        SelectedEntry = selectedKey is null
            ? null
            : Entries.FirstOrDefault(entry =>
                string.Equals(
                    entry.Key,
                    selectedKey,
                    StringComparison.OrdinalIgnoreCase));
        TargetDisplay = catalog.Target is null
            ? "No saved target"
            : $"{catalog.Target.ProcessName} · " +
              $"{catalog.Target.Architecture}";
        OnPropertyChanged(nameof(EntryCountDisplay));
        NotifyCommands();
    }

    private void OnCatalogChanged(
        object? sender,
        SavedAddressesChangedEventArgs eventArgs)
    {
        if (_synchronizationContext is not null &&
            SynchronizationContext.Current !=
            _synchronizationContext)
        {
            _synchronizationContext.Post(
                _ =>
                {
                    ApplyCatalog(eventArgs.Catalog);

                    if (_initialized)
                    {
                        _ = ValidateAsync();
                    }
                },
                null);
            return;
        }

        ApplyCatalog(eventArgs.Catalog);

        if (_initialized)
        {
            _ = ValidateAsync();
        }
    }

    private void OnSessionChanged(
        object? sender,
        MonitoringSessionChangedEventArgs eventArgs)
    {
        if (!_initialized)
        {
            return;
        }

        if (_synchronizationContext is not null &&
            SynchronizationContext.Current !=
            _synchronizationContext)
        {
            _synchronizationContext.Post(
                _ => _ = ValidateAsync(),
                null);
            return;
        }

        _ = ValidateAsync();
    }

    private void ApplyValidation(
        IReadOnlyDictionary<
            string,
            (
                SavedAddressReadStatus Status,
                string? Message,
                byte[]? Value)> validation)
    {
        _validation.Clear();

        foreach (var pair in validation)
        {
            _validation[pair.Key] = pair.Value;
        }

        ApplyCatalog(_savedAddressService.Catalog);
    }

    private MonitoringSession? GetConnectedSession()
    {
        var session =
            _monitoringSessionService.CurrentSession;
        return session?.State ==
            MonitoringSessionState.Connected
            ? session
            : null;
    }

    private void NotifyCommands()
    {
        AddCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        EditValueCommand.NotifyCanExecuteChanged();
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
}

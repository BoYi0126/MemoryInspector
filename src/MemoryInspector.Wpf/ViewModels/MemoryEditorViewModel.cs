using System.Buffers.Binary;
using System.Globalization;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Wpf.Mvvm;
using MemoryInspector.Wpf.Services;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class MemoryEditorViewModel :
    ObservableObject,
    IDisposable
{
    private readonly IMemoryEditorFeatureService _featureService;
    private readonly IMonitoringSessionService _sessionService;
    private readonly IMemoryReaderService _readerService;
    private readonly IMemoryRegionService _regionService;
    private readonly IMemoryValueSerializer _serializer;
    private readonly IMemoryWriteService _writeService;
    private readonly IMemoryWriteAuditService _auditService;
    private readonly IMemoryWriteAuditExportService _auditExportService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly IMemoryEditorFileDialogService _fileDialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly List<MemoryWriteHistoryRowViewModel> _allHistory = [];
    private CancellationTokenSource? _operationCancellation;
    private SuccessfulWrite? _lastSuccessfulWrite;
    private MemoryValueSerialization? _newValueSerialization;
    private byte[]? _currentBytes;
    private MemoryRegion? _region;
    private ulong? _loadedAddress;
    private MemoryWriteSource _source =
        MemoryWriteSource.ManualAddress;
    private bool _acknowledgesRisk;
    private bool _confirmsAuthorizedTargetsOnly;
    private bool _requireConfirmation = true;
    private bool _verifyAfterWrite = true;
    private bool _allowManualAddress;
    private bool _compareBeforeWrite = true;
    private bool _isBusy;
    private string _addressText = string.Empty;
    private ScanValueType _selectedValueType =
        ScanValueType.Int32;
    private MemoryEditorInputFormat _selectedInputFormat =
        MemoryEditorInputFormat.Decimal;
    private string _newValueText = string.Empty;
    private string _userNote = string.Empty;
    private string _targetProcessDisplay = "No connected target";
    private string _pidDisplay = "—";
    private string _sessionStatusDisplay = "Disconnected";
    private string _regionSummary = "No region loaded";
    private string _currentValueDisplay = "—";
    private string _currentBytesDisplay = "—";
    private string _parsedValueDisplay = "—";
    private string _hexadecimalPreview = "—";
    private string _newBytesPreview = "—";
    private string _byteOrderDisplay = "—";
    private string _writeByteCountDisplay = "—";
    private string? _inputErrorMessage;
    private string _statusMessage =
        "Enable Memory Editor and select an address source.";
    private string? _errorMessage;
    private string _resultStatusDisplay = "No write attempted";
    private string _writtenBytesDisplay = "—";
    private string _resultOriginalDisplay = "—";
    private string _resultRequestedDisplay = "—";
    private string _resultReadBackDisplay = "—";
    private string _verificationStatusDisplay = "—";
    private string _failureReasonDisplay = "—";
    private string _auditTimestampDisplay = "—";
    private string _historyFilterText = string.Empty;
    private IReadOnlyList<MemoryWriteHistoryRowViewModel> _history = [];
    private MemoryWriteHistoryRowViewModel? _selectedHistoryEntry;
    private bool _disposed;

    public MemoryEditorViewModel(
        IMemoryEditorFeatureService featureService,
        IMonitoringSessionService sessionService,
        IMemoryReaderService readerService,
        IMemoryRegionService regionService,
        IMemoryValueSerializer serializer,
        IMemoryWriteService writeService,
        IMemoryWriteAuditService auditService,
        IMemoryWriteAuditExportService auditExportService,
        IUserConfirmationService confirmationService,
        IMemoryEditorFileDialogService fileDialogService,
        IClipboardService clipboardService,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _featureService = Guard.NotNull(featureService);
        _sessionService = Guard.NotNull(sessionService);
        _readerService = Guard.NotNull(readerService);
        _regionService = Guard.NotNull(regionService);
        _serializer = Guard.NotNull(serializer);
        _writeService = Guard.NotNull(writeService);
        _auditService = Guard.NotNull(auditService);
        _auditExportService = Guard.NotNull(auditExportService);
        _confirmationService = Guard.NotNull(confirmationService);
        _fileDialogService = Guard.NotNull(fileDialogService);
        _clipboardService = Guard.NotNull(clipboardService);
        _logger = Guard.NotNull(logger);
        _timeProvider = Guard.NotNull(timeProvider);
        _synchronizationContext = SynchronizationContext.Current;

        EnableFeatureCommand = new AsyncRelayCommand(
            EnableFeatureAsync,
            () => !FeatureEnabled &&
                  AcknowledgesRisk &&
                  ConfirmsAuthorizedTargetsOnly &&
                  !IsBusy);
        DisableFeatureCommand = new AsyncRelayCommand(
            DisableFeatureAsync,
            () => FeatureEnabled && !IsBusy);
        LoadManualAddressCommand = new AsyncRelayCommand(
            LoadManualAddressAsync,
            () => FeatureEnabled &&
                  AllowManualAddress &&
                  !IsBusy);
        RefreshCurrentCommand = new AsyncRelayCommand(
            RefreshCurrentAsync,
            () => _loadedAddress.HasValue && !IsBusy);
        WriteCommand = new AsyncRelayCommand(
            WriteAsync,
            () => CanWrite);
        UndoLastWriteCommand = new AsyncRelayCommand(
            UndoLastWriteAsync,
            () => CanUndoLastWrite);
        CancelCommand = new RelayCommand(
            CancelOperation,
            () => IsBusy);
        RefreshHistoryCommand = new AsyncRelayCommand(
            () => LoadHistoryAsync(),
            () => !IsBusy);
        CopyHistoryCommand = new RelayCommand(
            CopySelectedHistory,
            () => SelectedHistoryEntry is not null);
        ExportHistoryCommand = new AsyncRelayCommand(
            ExportHistoryAsync,
            () => History.Count > 0 && !IsBusy);
        RetryFailedCommand = new AsyncRelayCommand(
            RetrySelectedAsync,
            () => SelectedHistoryEntry is not null &&
                  !SelectedHistoryEntry.Entry.Success &&
                  !IsBusy);

        _featureService.StateChanged += OnFeatureStateChanged;
        _sessionService.SessionChanged += OnSessionChanged;
        ApplyFeatureState(_featureService.State);
        ApplySession(_sessionService.CurrentSession);
    }

    public event EventHandler<MemoryWriteCompletedEventArgs>?
        WriteCompleted;

    public IReadOnlyList<ScanValueType> ValueTypes { get; } =
        Enum.GetValues<ScanValueType>();

    public IReadOnlyList<MemoryEditorInputFormat> InputFormats { get; } =
        Enum.GetValues<MemoryEditorInputFormat>();

    public string Purpose => MemoryEditorFeatureState.Purpose;

    public string RiskWarning => MemoryEditorFeatureState.RiskWarning;

    public string AuthorizedUseStatement =>
        MemoryEditorFeatureState.AuthorizedUseStatement;

    public bool FeatureEnabled => _featureService.State.IsEnabled;

    public bool CanConfigureFeature => !FeatureEnabled && !IsBusy;

    public string FeatureStatusDisplay =>
        FeatureEnabled
            ? $"Enabled at {_featureService.State.EnabledAt:u}"
            : "Disabled";

    public bool AcknowledgesRisk
    {
        get => _acknowledgesRisk;
        set
        {
            if (SetProperty(ref _acknowledgesRisk, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool ConfirmsAuthorizedTargetsOnly
    {
        get => _confirmsAuthorizedTargetsOnly;
        set
        {
            if (SetProperty(
                ref _confirmsAuthorizedTargetsOnly,
                value))
            {
                NotifyCommands();
            }
        }
    }

    public bool RequireConfirmation
    {
        get => _requireConfirmation;
        set => SetProperty(ref _requireConfirmation, value);
    }

    public bool VerifyAfterWrite
    {
        get => _verifyAfterWrite;
        set
        {
            if (SetProperty(ref _verifyAfterWrite, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool AllowManualAddress
    {
        get => _allowManualAddress;
        set
        {
            if (SetProperty(ref _allowManualAddress, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool CompareBeforeWrite
    {
        get => _compareBeforeWrite;
        set => SetProperty(ref _compareBeforeWrite, value);
    }

    public string AddressText
    {
        get => _addressText;
        set
        {
            if (SetProperty(
                ref _addressText,
                value ?? string.Empty))
            {
                NotifyCommands();
            }
        }
    }

    public ScanValueType SelectedValueType
    {
        get => _selectedValueType;
        set
        {
            if (SetProperty(ref _selectedValueType, value))
            {
                _loadedAddress = null;
                ValidateNewValue();
                NotifyCommands();
            }
        }
    }

    public MemoryEditorInputFormat SelectedInputFormat
    {
        get => _selectedInputFormat;
        set
        {
            if (SetProperty(ref _selectedInputFormat, value))
            {
                ValidateNewValue();
            }
        }
    }

    public string NewValueText
    {
        get => _newValueText;
        set
        {
            if (SetProperty(
                ref _newValueText,
                value ?? string.Empty))
            {
                ValidateNewValue();
            }
        }
    }

    public string UserNote
    {
        get => _userNote;
        set => SetProperty(
            ref _userNote,
            value ?? string.Empty);
    }

    public string TargetProcessDisplay
    {
        get => _targetProcessDisplay;
        private set => SetProperty(ref _targetProcessDisplay, value);
    }

    public string PidDisplay
    {
        get => _pidDisplay;
        private set => SetProperty(ref _pidDisplay, value);
    }

    public string SessionStatusDisplay
    {
        get => _sessionStatusDisplay;
        private set => SetProperty(ref _sessionStatusDisplay, value);
    }

    public string SourceDisplay => _source.ToString();

    public string RegionSummary
    {
        get => _regionSummary;
        private set => SetProperty(ref _regionSummary, value);
    }

    public string CurrentValueDisplay
    {
        get => _currentValueDisplay;
        private set => SetProperty(ref _currentValueDisplay, value);
    }

    public string CurrentBytesDisplay
    {
        get => _currentBytesDisplay;
        private set => SetProperty(ref _currentBytesDisplay, value);
    }

    public string ParsedValueDisplay
    {
        get => _parsedValueDisplay;
        private set => SetProperty(ref _parsedValueDisplay, value);
    }

    public string HexadecimalPreview
    {
        get => _hexadecimalPreview;
        private set => SetProperty(ref _hexadecimalPreview, value);
    }

    public string NewBytesPreview
    {
        get => _newBytesPreview;
        private set => SetProperty(ref _newBytesPreview, value);
    }

    public string ByteOrderDisplay
    {
        get => _byteOrderDisplay;
        private set => SetProperty(ref _byteOrderDisplay, value);
    }

    public string WriteByteCountDisplay
    {
        get => _writeByteCountDisplay;
        private set => SetProperty(ref _writeByteCountDisplay, value);
    }

    public string? InputErrorMessage
    {
        get => _inputErrorMessage;
        private set => SetProperty(ref _inputErrorMessage, value);
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

    public bool CanWrite =>
        FeatureEnabled &&
        _sessionService.CurrentSession?.State ==
            MonitoringSessionState.Connected &&
        _loadedAddress.HasValue &&
        _currentBytes is not null &&
        _region?.IsWritable == true &&
        _newValueSerialization is not null &&
        !IsBusy;

    public bool CanUndoLastWrite =>
        FeatureEnabled &&
        _lastSuccessfulWrite is not null &&
        !IsBusy;

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

    public string ResultStatusDisplay
    {
        get => _resultStatusDisplay;
        private set => SetProperty(ref _resultStatusDisplay, value);
    }

    public string WrittenBytesDisplay
    {
        get => _writtenBytesDisplay;
        private set => SetProperty(ref _writtenBytesDisplay, value);
    }

    public string ResultOriginalDisplay
    {
        get => _resultOriginalDisplay;
        private set => SetProperty(ref _resultOriginalDisplay, value);
    }

    public string ResultRequestedDisplay
    {
        get => _resultRequestedDisplay;
        private set => SetProperty(ref _resultRequestedDisplay, value);
    }

    public string ResultReadBackDisplay
    {
        get => _resultReadBackDisplay;
        private set => SetProperty(ref _resultReadBackDisplay, value);
    }

    public string VerificationStatusDisplay
    {
        get => _verificationStatusDisplay;
        private set => SetProperty(ref _verificationStatusDisplay, value);
    }

    public string FailureReasonDisplay
    {
        get => _failureReasonDisplay;
        private set => SetProperty(ref _failureReasonDisplay, value);
    }

    public string AuditTimestampDisplay
    {
        get => _auditTimestampDisplay;
        private set => SetProperty(ref _auditTimestampDisplay, value);
    }

    public string HistoryFilterText
    {
        get => _historyFilterText;
        set
        {
            if (SetProperty(
                ref _historyFilterText,
                value ?? string.Empty))
            {
                ApplyHistoryFilter();
            }
        }
    }

    public IReadOnlyList<MemoryWriteHistoryRowViewModel> History
    {
        get => _history;
        private set
        {
            if (SetProperty(ref _history, value))
            {
                ExportHistoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public MemoryWriteHistoryRowViewModel? SelectedHistoryEntry
    {
        get => _selectedHistoryEntry;
        set
        {
            if (SetProperty(ref _selectedHistoryEntry, value))
            {
                CopyHistoryCommand.NotifyCanExecuteChanged();
                RetryFailedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand EnableFeatureCommand { get; }

    public AsyncRelayCommand DisableFeatureCommand { get; }

    public AsyncRelayCommand LoadManualAddressCommand { get; }

    public AsyncRelayCommand RefreshCurrentCommand { get; }

    public AsyncRelayCommand WriteCommand { get; }

    public AsyncRelayCommand UndoLastWriteCommand { get; }

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand RefreshHistoryCommand { get; }

    public RelayCommand CopyHistoryCommand { get; }

    public AsyncRelayCommand ExportHistoryCommand { get; }

    public AsyncRelayCommand RetryFailedCommand { get; }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await LoadHistoryAsync(cancellationToken);
    }

    public async Task OpenAsync(
        ulong address,
        ScanValueType valueType,
        MemoryWriteSource source,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AddressText = $"0x{address:X16}";
        _selectedValueType = valueType;
        OnPropertyChanged(nameof(SelectedValueType));
        _source = source;
        OnPropertyChanged(nameof(SourceDisplay));
        NewValueText = string.Empty;
        await LoadContextAsync(
            address,
            valueType,
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _featureService.StateChanged -= OnFeatureStateChanged;
        _sessionService.SessionChanged -= OnSessionChanged;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _disposed = true;
    }

    private async Task EnableFeatureAsync()
    {
        var message =
            $"{Purpose}\n\n{RiskWarning}\n\n" +
            $"{AuthorizedUseStatement}\n\n" +
            "Enable Memory Editor with the selected safety settings?";

        if (!_confirmationService.Confirm(
            "Enable Memory Editor",
            message))
        {
            return;
        }

        await RunBusyAsync(async token =>
        {
            var result = await _featureService.EnableAsync(
                new MemoryEditorEnablementAcknowledgement(
                    AcknowledgesRisk,
                    ConfirmsAuthorizedTargetsOnly),
                RequireConfirmation,
                VerifyAfterWrite,
                AllowManualAddress,
                token);
            ApplyFeatureResult(result);
        });
    }

    private async Task DisableFeatureAsync()
    {
        if (!_confirmationService.Confirm(
            "Disable Memory Editor",
            "Disable all Memory Editor write operations?"))
        {
            return;
        }

        await RunBusyAsync(async token =>
        {
            var result = await _featureService.DisableAsync(token);
            ApplyFeatureResult(result);
        });
    }

    private async Task LoadManualAddressAsync()
    {
        if (!AllowManualAddress)
        {
            ApplyFailure(
                "Manual-address editing is disabled.");
            return;
        }

        if (!TryParseAddress(AddressText, out var address))
        {
            ApplyFailure(
                "Address must be a valid x64 hexadecimal value.");
            return;
        }

        _source = MemoryWriteSource.ManualAddress;
        OnPropertyChanged(nameof(SourceDisplay));
        await LoadContextAsync(
            address,
            SelectedValueType,
            CancellationToken.None);
    }

    private Task RefreshCurrentAsync()
    {
        return _loadedAddress.HasValue
            ? LoadContextAsync(
                _loadedAddress.Value,
                SelectedValueType,
                CancellationToken.None)
            : Task.CompletedTask;
    }

    private async Task LoadContextAsync(
        ulong address,
        ScanValueType valueType,
        CancellationToken cancellationToken)
    {
        await RunBusyAsync(async token =>
        {
            var session = _sessionService.CurrentSession;

            if (session?.State != MonitoringSessionState.Connected)
            {
                ClearLoadedContext();
                ApplyFailure(
                    "A connected Monitoring Session is required.");
                return;
            }

            var length = ScanValueTypeInfo.GetSize(valueType);
            var regionsTask = _regionService.GetRegionsAsync(token);
            var readTask = _readerService.ReadAsync(
                address,
                length,
                cancellationToken: token);
            await Task.WhenAll(regionsTask, readTask);
            var regions = await regionsTask;
            var read = await readTask;

            if (regions.IsFailure)
            {
                ClearLoadedContext();
                ApplyFailure(
                    "Memory regions could not be loaded.",
                    regions.Error);
                return;
            }

            var endAddress = checked(address + (ulong)length);
            _region = regions.Value.Regions.FirstOrDefault(region =>
                address >= region.BaseAddress &&
                endAddress <= region.EndAddress);

            if (_region is null)
            {
                ClearLoadedContext();
                ApplyFailure(
                    "Address is not contained in a valid memory region.");
                return;
            }

            RegionSummary =
                $"0x{_region.BaseAddress:X16}–" +
                $"0x{_region.EndAddress - 1:X16} · " +
                $"{_region.State} · {_region.Protection} · " +
                (_region.IsWritable ? "Writable" : "Not writable");

            if (!_region.IsWritable)
            {
                _currentBytes = null;
                _loadedAddress = address;
                ApplyFailure("The selected memory region is not writable.");
                return;
            }

            if (read.IsFailure ||
                !read.Value.IsComplete ||
                read.Value.Data.Length != length)
            {
                _currentBytes = null;
                _loadedAddress = address;
                ApplyFailure(
                    "The current value could not be read.",
                    read.IsFailure
                        ? read.Error
                        : new Error(
                            ErrorCode.NativeApi,
                            "A complete value was not returned."));
                return;
            }

            _currentBytes = read.Value.Data.ToArray();
            _loadedAddress = address;
            CurrentValueDisplay =
                ResultGridRowViewModel.FormatValue(
                    valueType,
                    _currentBytes);
            CurrentBytesDisplay = FormatBytes(_currentBytes);
            AddressText = $"0x{address:X16}";
            StatusMessage =
                $"Loaded {AddressText} from {_source}.";
            ErrorMessage = null;
            NotifyCommands();
        }, cancellationToken);
    }

    private async Task WriteAsync()
    {
        if (!CanWrite ||
            _newValueSerialization is null ||
            _currentBytes is null ||
            !_loadedAddress.HasValue)
        {
            ApplyFailure(
                "The Memory Editor is not ready to write.");
            return;
        }

        var session = _sessionService.CurrentSession!;
        var confirmation = new MemoryWriteConfirmation(
            session.Identity,
            _loadedAddress.Value,
            RegionSummary,
            SelectedValueType,
            CurrentValueDisplay,
            _newValueSerialization.DecimalPreview,
            CurrentBytesDisplay,
            _newValueSerialization.BytePreview,
            VerifyAfterWrite);

        if (!_confirmationService.Confirm(
            "Confirm memory write",
            BuildConfirmationMessage(confirmation)))
        {
            StatusMessage = "Memory write was not confirmed.";
            return;
        }

        var request = new MemoryWriteRequest(
            session.SessionId,
            session.Identity,
            _loadedAddress.Value,
            SelectedValueType,
            _newValueSerialization.InputText,
            _newValueSerialization.Bytes.Span,
            CompareBeforeWrite ? _currentBytes : [],
            CompareBeforeWrite,
            VerifyAfterWrite,
            _source,
            UserNote,
            _timeProvider.GetUtcNow());
        await ExecuteWriteAsync(request, rememberForUndo: true);
    }

    private async Task UndoLastWriteAsync()
    {
        var last = _lastSuccessfulWrite;

        if (last is null ||
            !last.Result.OriginalValue.HasValue)
        {
            return;
        }

        await RunBusyAsync(async token =>
        {
            var session = _sessionService.CurrentSession;

            if (session?.State != MonitoringSessionState.Connected ||
                session.SessionId != last.Request.SessionId ||
                session.Identity != last.Request.TargetIdentity)
            {
                ApplyFailure(
                    "Undo requires the original connected session.");
                return;
            }

            var current = await _readerService.ReadAsync(
                last.Request.Address,
                last.Request.ParsedBytes.Length,
                cancellationToken: token);

            if (current.IsFailure || !current.Value.IsComplete)
            {
                ApplyFailure(
                    "Undo could not read the current value.",
                    current.IsFailure
                        ? current.Error
                        : new Error(
                            ErrorCode.NativeApi,
                            "A complete current value was not returned."));
                return;
            }

            var currentBytes = current.Value.Data.ToArray();
            var conflict = !currentBytes.AsSpan().SequenceEqual(
                last.Request.ParsedBytes.Span);

            if (conflict &&
                !_confirmationService.Confirm(
                    "Undo conflict",
                    "The current value no longer matches the last " +
                    "requested value.\n\n" +
                    $"Current: {FormatBytes(currentBytes)}\n" +
                    $"Expected: " +
                    $"{FormatBytes(last.Request.ParsedBytes.Span)}\n\n" +
                    "Attempt the undo using the current value as " +
                    "the compare-before-write value?"))
            {
                StatusMessage = "Undo was cancelled because of a conflict.";
                return;
            }

            var original = last.Result.OriginalValue.Value;
            var originalDisplay =
                ResultGridRowViewModel.FormatValue(
                    last.Request.ValueType,
                    original.Span);
            var confirmation = new MemoryWriteConfirmation(
                session.Identity,
                last.Request.Address,
                RegionSummary,
                last.Request.ValueType,
                ResultGridRowViewModel.FormatValue(
                    last.Request.ValueType,
                    currentBytes),
                originalDisplay,
                FormatBytes(currentBytes),
                FormatBytes(original.Span),
                VerifyAfterWrite);

            if (!_confirmationService.Confirm(
                "Confirm undo memory write",
                BuildConfirmationMessage(confirmation)))
            {
                StatusMessage = "Undo memory write was not confirmed.";
                return;
            }

            var request = new MemoryWriteRequest(
                session.SessionId,
                session.Identity,
                last.Request.Address,
                last.Request.ValueType,
                originalDisplay,
                original.Span,
                currentBytes,
                hasExpectedOriginalValue: true,
                VerifyAfterWrite,
                last.Request.Source,
                $"Undo: {last.Request.UserNote ?? "last write"}",
                _timeProvider.GetUtcNow());
            var result = await _writeService.WriteAsync(request, token);
            ApplyWriteResult(result);
            await LoadHistoryCoreAsync(token);

            if (result.Success)
            {
                _lastSuccessfulWrite = null;
                WriteCompleted?.Invoke(
                    this,
                    new MemoryWriteCompletedEventArgs(
                        request,
                        result));
                await ReloadCurrentAfterWriteAsync(request, token);
            }
        });
    }

    private async Task ExecuteWriteAsync(
        MemoryWriteRequest request,
        bool rememberForUndo)
    {
        await RunBusyAsync(async token =>
        {
            var result = await _writeService.WriteAsync(request, token);
            ApplyWriteResult(result);
            await LoadHistoryCoreAsync(token);

            if (result.Success)
            {
                if (rememberForUndo)
                {
                    _lastSuccessfulWrite =
                        new SuccessfulWrite(request, result);
                }

                WriteCompleted?.Invoke(
                    this,
                    new MemoryWriteCompletedEventArgs(
                        request,
                        result));
                await ReloadCurrentAfterWriteAsync(request, token);
            }
        });
    }

    private async Task ReloadCurrentAfterWriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken)
    {
        var read = await _readerService.ReadAsync(
            request.Address,
            request.ParsedBytes.Length,
            cancellationToken: cancellationToken);

        if (read.IsSuccess && read.Value.IsComplete)
        {
            _currentBytes = read.Value.Data.ToArray();
            CurrentBytesDisplay = FormatBytes(_currentBytes);
            CurrentValueDisplay =
                ResultGridRowViewModel.FormatValue(
                    request.ValueType,
                    _currentBytes);
        }
    }

    private void ApplyWriteResult(MemoryWriteResult result)
    {
        ResultStatusDisplay = result.Success
            ? "Success"
            : "Failure";
        WrittenBytesDisplay =
            $"{result.WrittenByteCount} / " +
            $"{result.RequestedByteCount}";
        ResultOriginalDisplay = FormatOptionalBytes(
            result.OriginalValue);
        ResultRequestedDisplay =
            FormatBytes(result.RequestedValue.Span);
        ResultReadBackDisplay = FormatOptionalBytes(
            result.ReadBackValue);
        VerificationStatusDisplay =
            result.Verification.Status.ToString();
        FailureReasonDisplay = result.Success
            ? "None"
            : FriendlyFailure(result.FailureReason);
        AuditTimestampDisplay = result.CompletedAt
            .ToLocalTime()
            .ToString(
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture);
        StatusMessage = result.Success
            ? "Memory write completed and was audited."
            : FriendlyFailure(result.FailureReason);
        ErrorMessage = result.Success
            ? null
            : result.Error.ToDisplayMessage();
        NotifyCommands();
    }

    private async Task LoadHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await RunBusyAsync(
            token => LoadHistoryCoreAsync(token),
            cancellationToken);
    }

    private async Task LoadHistoryCoreAsync(
        CancellationToken cancellationToken)
    {
        var result = await _auditService.ReadRecentAsync(
            cancellationToken: cancellationToken);

        if (result.IsFailure)
        {
            ApplyFailure(
                "Write history could not be loaded.",
                result.Error);
            return;
        }

        _allHistory.Clear();
        _allHistory.AddRange(
            result.Value.Select(
                entry =>
                    new MemoryWriteHistoryRowViewModel(entry)));
        ApplyHistoryFilter();
    }

    private void ApplyHistoryFilter()
    {
        var selectedId = SelectedHistoryEntry?.Entry.AuditId;
        History = Array.AsReadOnly(
            _allHistory
                .Where(row => row.Matches(HistoryFilterText))
                .ToArray());
        SelectedHistoryEntry = selectedId.HasValue
            ? History.FirstOrDefault(row =>
                row.Entry.AuditId == selectedId.Value)
            : null;
    }

    private void CopySelectedHistory()
    {
        if (SelectedHistoryEntry is null)
        {
            return;
        }

        var result = _clipboardService.SetText(
            SelectedHistoryEntry.CopySummary);
        StatusMessage = result.IsSuccess
            ? "Audit summary copied."
            : result.Error.ToDisplayMessage();
        ErrorMessage = result.IsFailure
            ? result.Error.ToDisplayMessage()
            : null;
    }

    private async Task ExportHistoryAsync()
    {
        var path = _fileDialogService.SelectAuditExportFile(
            $"memory-editor-audit-" +
            $"{_timeProvider.GetUtcNow():yyyyMMdd-HHmmss}.csv");

        if (path is null)
        {
            return;
        }

        await RunBusyAsync(async token =>
        {
            var entries = History
                .Select(row => row.Entry)
                .ToArray();
            var result = await _auditExportService.ExportSummaryAsync(
                path,
                entries,
                token);
            StatusMessage = result.IsSuccess
                ? $"Exported {entries.Length:N0} audit entries."
                : result.Error.ToDisplayMessage();
            ErrorMessage = result.IsFailure
                ? result.Error.ToDisplayMessage()
                : null;
        });
    }

    private async Task RetrySelectedAsync()
    {
        var entry = SelectedHistoryEntry?.Entry;

        if (entry is null || entry.Success)
        {
            return;
        }

        await OpenAsync(
            entry.Address,
            entry.ValueType,
            entry.Source);
        SelectedInputFormat = MemoryEditorInputFormat.Decimal;
        NewValueText = ResultGridRowViewModel.FormatValue(
            entry.ValueType,
            entry.RequestedValue.Span);
        UserNote = $"Retry: {entry.UserNote ?? entry.FailureReason.ToString()}";
        StatusMessage =
            "Failed request loaded. Review current value and confirm again.";
    }

    private void ValidateNewValue()
    {
        _newValueSerialization = null;

        if (string.IsNullOrWhiteSpace(NewValueText))
        {
            ResetNewValuePreview();
            InputErrorMessage = null;
            NotifyCommands();
            return;
        }

        var normalized = NormalizeInput(
            NewValueText,
            SelectedValueType,
            SelectedInputFormat);

        if (normalized.IsFailure)
        {
            ResetNewValuePreview();
            InputErrorMessage =
                normalized.Error.ToDisplayMessage();
            NotifyCommands();
            return;
        }

        var serialized = _serializer.Serialize(
            normalized.Value,
            SelectedValueType);

        if (serialized.IsFailure)
        {
            ResetNewValuePreview();
            InputErrorMessage =
                serialized.Error.ToDisplayMessage();
            NotifyCommands();
            return;
        }

        _newValueSerialization = serialized.Value;
        ParsedValueDisplay = serialized.Value.DecimalPreview;
        HexadecimalPreview = serialized.Value.HexadecimalPreview;
        NewBytesPreview = serialized.Value.BytePreview;
        ByteOrderDisplay = serialized.Value.ByteOrder.ToString();
        WriteByteCountDisplay =
            $"{serialized.Value.Bytes.Length} byte(s)";
        InputErrorMessage = null;
        NotifyCommands();
    }

    private static Result<string> NormalizeInput(
        string input,
        ScanValueType valueType,
        MemoryEditorInputFormat format)
    {
        var trimmed = input.Trim();

        if (format == MemoryEditorInputFormat.Decimal)
        {
            return Result<string>.Success(trimmed);
        }

        if (valueType is ScanValueType.Float or ScanValueType.Double)
        {
            var digits = RemoveHexPrefix(trimmed);
            var requiredDigits =
                ScanValueTypeInfo.GetSize(valueType) * 2;

            if (digits.Length == 0 ||
                digits.Length > requiredDigits ||
                !ulong.TryParse(
                    digits,
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out var raw))
            {
                return InvalidInput(
                    "Floating-point hexadecimal input must be an " +
                    $"IEEE-754 bit pattern up to {requiredDigits} digits.");
            }

            var value = valueType == ScanValueType.Float
                ? BitConverter.Int32BitsToSingle((int)(uint)raw)
                    .ToString("R", CultureInfo.InvariantCulture)
                : BitConverter.Int64BitsToDouble((long)raw)
                    .ToString("R", CultureInfo.InvariantCulture);
            return Result<string>.Success(value);
        }

        if (trimmed.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(
                "+0x",
                StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(
                "-0x",
                StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.Success(trimmed);
        }

        return trimmed.StartsWith('+') ||
               trimmed.StartsWith('-')
            ? Result<string>.Success(
                $"{trimmed[0]}0x{trimmed[1..]}")
            : Result<string>.Success($"0x{trimmed}");
    }

    private static string RemoveHexPrefix(string input)
    {
        return input.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase)
                ? input[2..]
                : input;
    }

    private static Result<string> InvalidInput(string message)
    {
        return Result<string>.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private void ResetNewValuePreview()
    {
        ParsedValueDisplay = "—";
        HexadecimalPreview = "—";
        NewBytesPreview = "—";
        ByteOrderDisplay = "—";
        WriteByteCountDisplay = "—";
    }

    private async Task RunBusyAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var current = _operationCancellation;
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await action(current.Token);
        }
        catch (OperationCanceledException)
            when (current.IsCancellationRequested)
        {
            StatusMessage = "Memory Editor operation was cancelled.";
        }
        catch (Exception exception)
        {
            ApplyFailure(
                "Memory Editor operation failed unexpectedly.",
                new Error(
                    ErrorCode.Unexpected,
                    exception.Message,
                    exception));
        }
        finally
        {
            if (ReferenceEquals(
                _operationCancellation,
                current))
            {
                _operationCancellation = null;
                current.Dispose();
                IsBusy = false;
            }
        }
    }

    private void CancelOperation()
    {
        _operationCancellation?.Cancel();
    }

    private void ApplyFeatureResult(
        Result<MemoryEditorFeatureState> result)
    {
        if (result.IsFailure)
        {
            ApplyFailure(
                "Memory Editor settings could not be changed.",
                result.Error);
            return;
        }

        ApplyFeatureState(result.Value);
        StatusMessage = result.Value.IsEnabled
            ? "Memory Editor enabled."
            : "Memory Editor disabled.";
        ErrorMessage = null;
    }

    private void ApplyFeatureState(
        MemoryEditorFeatureState state)
    {
        RequireConfirmation =
            state.Settings.RequireConfirmation;
        VerifyAfterWrite =
            state.Settings.VerifyAfterWrite;
        AllowManualAddress =
            state.Settings.AllowManualAddress;
        OnPropertyChanged(nameof(FeatureEnabled));
        OnPropertyChanged(nameof(FeatureStatusDisplay));
        NotifyCommands();
    }

    private void ApplySession(MonitoringSession? session)
    {
        TargetProcessDisplay = session is null
            ? "No connected target"
            : session.Identity.ProcessName;
        PidDisplay = session is null
            ? "—"
            : session.Identity.ProcessId.ToString(
                CultureInfo.InvariantCulture);
        SessionStatusDisplay =
            session?.State.ToString() ?? "Disconnected";

        if (session?.State != MonitoringSessionState.Connected)
        {
            ClearLoadedContext();
        }

        NotifyCommands();
    }

    private void OnFeatureStateChanged(
        object? sender,
        MemoryEditorFeatureChangedEventArgs eventArgs)
    {
        PostToContext(() => ApplyFeatureState(eventArgs.State));
    }

    private void OnSessionChanged(
        object? sender,
        MonitoringSessionChangedEventArgs eventArgs)
    {
        PostToContext(() => ApplySession(eventArgs.Session));
    }

    private void PostToContext(Action action)
    {
        if (_synchronizationContext is not null &&
            SynchronizationContext.Current !=
                _synchronizationContext)
        {
            _synchronizationContext.Post(_ => action(), null);
            return;
        }

        action();
    }

    private void ClearLoadedContext()
    {
        _loadedAddress = null;
        _currentBytes = null;
        _region = null;
        RegionSummary = "No region loaded";
        CurrentValueDisplay = "—";
        CurrentBytesDisplay = "—";
        NotifyCommands();
    }

    private void ApplyFailure(
        string message,
        Error? error = null)
    {
        ErrorMessage = error is null
            ? message
            : $"{message} {error.ToDisplayMessage()}";
        StatusMessage = ErrorMessage;

        if (error?.Code != ErrorCode.Cancelled)
        {
            _ = _logger.Log(
                AppLogLevel.Warning,
                ErrorMessage,
                error?.Exception);
        }
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(CanUndoLastWrite));
        OnPropertyChanged(nameof(CanConfigureFeature));
        EnableFeatureCommand.NotifyCanExecuteChanged();
        DisableFeatureCommand.NotifyCanExecuteChanged();
        LoadManualAddressCommand.NotifyCanExecuteChanged();
        RefreshCurrentCommand.NotifyCanExecuteChanged();
        WriteCommand.NotifyCanExecuteChanged();
        UndoLastWriteCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RefreshHistoryCommand.NotifyCanExecuteChanged();
        ExportHistoryCommand.NotifyCanExecuteChanged();
        RetryFailedCommand.NotifyCanExecuteChanged();
    }

    private static string BuildConfirmationMessage(
        MemoryWriteConfirmation confirmation)
    {
        return
            $"Process: {confirmation.TargetIdentity.ProcessName} " +
            $"(PID {confirmation.TargetIdentity.ProcessId})\n" +
            $"Address: 0x{confirmation.Address:X16}\n" +
            $"Region: {confirmation.RegionSummary ?? "Unknown"}\n" +
            $"Type: {confirmation.ValueType}\n" +
            $"Original value: {confirmation.OriginalValue}\n" +
            $"New value: {confirmation.NewValue}\n" +
            $"Original bytes: {confirmation.OriginalBytes}\n" +
            $"New bytes: {confirmation.NewBytes}\n" +
            $"Verify after write: " +
            $"{(confirmation.VerifyAfterWrite ? "Yes" : "No")}\n\n" +
            "Perform this single authorized memory write?";
    }

    private static string FriendlyFailure(
        MemoryWriteFailureReason reason)
    {
        return reason switch
        {
            MemoryWriteFailureReason.TargetExited =>
                "The target process has exited.",
            MemoryWriteFailureReason.InvalidAddress or
            MemoryWriteFailureReason.RegionNotFound =>
                "The address is not in a valid memory region.",
            MemoryWriteFailureReason.RegionNotCommitted =>
                "The memory region is not committed.",
            MemoryWriteFailureReason.RegionNotWritable or
            MemoryWriteFailureReason.GuardPage =>
                "The memory region is not writable.",
            MemoryWriteFailureReason.OriginalValueMismatch =>
                "The original value changed before writing.",
            MemoryWriteFailureReason.PartialWrite =>
                "Only part of the requested value was written.",
            MemoryWriteFailureReason.VerificationReadFailed or
            MemoryWriteFailureReason.VerificationMismatch =>
                "Write verification failed.",
            MemoryWriteFailureReason.AccessDenied =>
                "Access to the target process was denied.",
            MemoryWriteFailureReason.SessionInvalid =>
                "The Monitoring Session is no longer valid.",
            MemoryWriteFailureReason.FeatureDisabled =>
                "Memory Editor is disabled.",
            MemoryWriteFailureReason.Cancelled =>
                "The memory write was cancelled.",
            _ => reason.ToString(),
        };
    }

    private static bool TryParseAddress(
        string input,
        out ulong address)
    {
        address = 0;
        var text = (input ?? string.Empty).Trim();

        if (text.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return text.Length > 0 &&
               ulong.TryParse(
                   text,
                   NumberStyles.AllowHexSpecifier,
                   CultureInfo.InvariantCulture,
                   out address);
    }

    private static string FormatOptionalBytes(
        ReadOnlyMemory<byte>? value)
    {
        return value.HasValue
            ? FormatBytes(value.Value.Span)
            : "—";
    }

    private static string FormatBytes(ReadOnlySpan<byte> value)
    {
        return string.Join(
            " ",
            value.ToArray().Select(
                item => item.ToString("X2")));
    }

    private sealed record SuccessfulWrite(
        MemoryWriteRequest Request,
        MemoryWriteResult Result);
}

using System.Globalization;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Wpf.Mvvm;
using MemoryInspector.Wpf.Services;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class ScanWorkspaceViewModel :
    ObservableObject,
    IDisposable
{
    private readonly IScanWorkflowService _workflow;
    private readonly IScanValueParser _parser;
    private readonly IMonitoringSessionService _sessionService;
    private readonly IUserConfirmationService _confirmation;
    private readonly IAppLogger _logger;
    private readonly SynchronizationContext? _synchronizationContext;
    private CancellationTokenSource? _operationCancellation;
    private MonitoringSession? _currentSession;
    private FilterPipelineState? _pipelineState;
    private UnknownInitialScanEstimate? _unknownEstimate;
    private FirstScanMode _selectedFirstScanMode =
        FirstScanMode.ExactValue;
    private ScanValueType _selectedValueType = ScanValueType.Int32;
    private ScanComparisonMode _selectedComparisonMode =
        ScanComparisonMode.ExactValue;
    private ScanAlignmentMode _selectedAlignmentMode =
        ScanAlignmentMode.Aligned;
    private string _valueText = string.Empty;
    private string _toleranceText =
        ScanRequest.DefaultFloatTolerance.ToString(
            "R",
            CultureInfo.InvariantCulture);
    private int _maximumResults = ScanRequest.DefaultMaximumResults;
    private bool _isBusy;
    private long _progressCompleted;
    private long? _progressTotal;
    private string _progressStage = string.Empty;
    private string _statusMessage =
        "Start monitoring a process before scanning memory.";
    private string? _warningMessage;
    private string? _errorMessage;
    private bool _disposed;

    public ScanWorkspaceViewModel(
        IScanWorkflowService workflow,
        IScanValueParser parser,
        IMonitoringSessionService sessionService,
        IUserConfirmationService confirmation,
        IAppLogger logger)
    {
        _workflow = Guard.NotNull(workflow);
        _parser = Guard.NotNull(parser);
        _sessionService = Guard.NotNull(sessionService);
        _confirmation = Guard.NotNull(confirmation);
        _logger = Guard.NotNull(logger);
        _synchronizationContext = SynchronizationContext.Current;
        _currentSession = sessionService.CurrentSession;
        _pipelineState = workflow.CurrentState;

        EstimateUnknownCommand = new AsyncRelayCommand(
            EstimateUnknownAsync,
            () => CanEstimateUnknown);
        FirstScanCommand = new AsyncRelayCommand(
            FirstScanAsync,
            () => CanFirstScan);
        NextScanCommand = new AsyncRelayCommand(
            NextScanAsync,
            () => CanNextScan);
        KeepCommand = new AsyncRelayCommand(
            KeepAsync,
            () => CanKeep);
        DiscardCommand = new AsyncRelayCommand(
            DiscardAsync,
            () => CanDiscard);
        CancelCommand = new RelayCommand(
            Cancel,
            () => IsBusy);
        NewScanCommand = new RelayCommand(
            NewScan,
            () => !IsBusy && IsSessionConnected);
        ViewResultsCommand = new RelayCommand(
            ViewResults,
            () => CurrentSnapshot is not null && !IsBusy);

        sessionService.SessionChanged += OnSessionChanged;
        UpdateReadyStatus();
    }

    public event EventHandler<ScanSnapshotReadyEventArgs>? SnapshotReady;

    public IReadOnlyList<FirstScanMode> FirstScanModes { get; } =
        Enum.GetValues<FirstScanMode>();

    public IReadOnlyList<ScanValueType> ValueTypes { get; } =
        Enum.GetValues<ScanValueType>();

    public IReadOnlyList<ScanAlignmentMode> AlignmentModes { get; } =
        Enum.GetValues<ScanAlignmentMode>();

    public IReadOnlyList<ScanComparisonMode> NextComparisonModes { get; } =
    [
        ScanComparisonMode.ExactValue,
        ScanComparisonMode.Changed,
        ScanComparisonMode.Unchanged,
        ScanComparisonMode.Increased,
        ScanComparisonMode.Decreased,
        ScanComparisonMode.GreaterThan,
        ScanComparisonMode.LessThan,
    ];

    public FirstScanMode SelectedFirstScanMode
    {
        get => _selectedFirstScanMode;
        set
        {
            if (SetProperty(ref _selectedFirstScanMode, value))
            {
                UnknownEstimate = null;
                NotifyState();
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
                UnknownEstimate = null;
                OnPropertyChanged(nameof(UsesFloatingPointTolerance));
                NotifyState();
            }
        }
    }

    public ScanComparisonMode SelectedComparisonMode
    {
        get => _selectedComparisonMode;
        set
        {
            if (SetProperty(ref _selectedComparisonMode, value))
            {
                OnPropertyChanged(nameof(NextScanRequiresValue));
                NotifyState();
            }
        }
    }

    public ScanAlignmentMode SelectedAlignmentMode
    {
        get => _selectedAlignmentMode;
        set
        {
            if (SetProperty(ref _selectedAlignmentMode, value))
            {
                UnknownEstimate = null;
                NotifyState();
            }
        }
    }

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (SetProperty(ref _valueText, value ?? string.Empty))
            {
                NotifyState();
            }
        }
    }

    public string ToleranceText
    {
        get => _toleranceText;
        set
        {
            if (SetProperty(ref _toleranceText, value ?? string.Empty))
            {
                NotifyState();
            }
        }
    }

    public int MaximumResults
    {
        get => _maximumResults;
        set
        {
            if (SetProperty(ref _maximumResults, value))
            {
                NotifyState();
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
                NotifyState();
            }
        }
    }

    public FilterPipelineState? PipelineState
    {
        get => _pipelineState;
        private set
        {
            if (SetProperty(ref _pipelineState, value))
            {
                OnPropertyChanged(nameof(HasActiveScan));
                OnPropertyChanged(nameof(CanConfigureFirstScan));
                OnPropertyChanged(nameof(ActiveCandidateDisplay));
                OnPropertyChanged(nameof(PendingCandidateDisplay));
                OnPropertyChanged(nameof(RemovedCandidateDisplay));
                OnPropertyChanged(nameof(CurrentSnapshot));
                NotifyState();
            }
        }
    }

    public UnknownInitialScanEstimate? UnknownEstimate
    {
        get => _unknownEstimate;
        private set
        {
            if (SetProperty(ref _unknownEstimate, value))
            {
                OnPropertyChanged(nameof(EstimateDisplay));
            }
        }
    }

    public long ProgressCompleted
    {
        get => _progressCompleted;
        private set
        {
            if (SetProperty(ref _progressCompleted, value))
            {
                OnPropertyChanged(nameof(ProgressPercentage));
                OnPropertyChanged(nameof(ProgressDisplay));
            }
        }
    }

    public long? ProgressTotal
    {
        get => _progressTotal;
        private set
        {
            if (SetProperty(ref _progressTotal, value))
            {
                OnPropertyChanged(nameof(ProgressPercentage));
                OnPropertyChanged(nameof(IsProgressIndeterminate));
                OnPropertyChanged(nameof(ProgressDisplay));
            }
        }
    }

    public string ProgressStage
    {
        get => _progressStage;
        private set => SetProperty(ref _progressStage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? WarningMessage
    {
        get => _warningMessage;
        private set => SetProperty(ref _warningMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsSessionConnected =>
        _currentSession?.State == MonitoringSessionState.Connected;

    public bool HasActiveScan => PipelineState is not null;

    public bool CanConfigureFirstScan => !HasActiveScan && !IsBusy;

    public bool IsUnknownFirstScan =>
        SelectedFirstScanMode ==
        FirstScanMode.UnknownInitialValue;

    public bool UsesFloatingPointTolerance =>
        SelectedValueType is
            ScanValueType.Float or
            ScanValueType.Double;

    public bool NextScanRequiresValue =>
        RequiresValue(SelectedComparisonMode);

    public bool CanEstimateUnknown =>
        IsSessionConnected &&
        !IsBusy &&
        !HasActiveScan &&
        IsUnknownFirstScan;

    public bool CanFirstScan =>
        IsSessionConnected &&
        !IsBusy &&
        !HasActiveScan &&
        MaximumResults > 0 &&
        (IsUnknownFirstScan ||
         !string.IsNullOrWhiteSpace(ValueText));

    public bool CanNextScan =>
        IsSessionConnected &&
        !IsBusy &&
        PipelineState?.CanContinueFiltering == true &&
        (!NextScanRequiresValue ||
         !string.IsNullOrWhiteSpace(ValueText));

    public bool CanKeep =>
        !IsBusy && PipelineState?.CanKeep == true;

    public bool CanDiscard =>
        !IsBusy && PipelineState?.CanDiscard == true;

    public bool IsProgressIndeterminate =>
        IsBusy && !ProgressTotal.HasValue;

    public double ProgressPercentage => ProgressTotal switch
    {
        > 0 => Math.Clamp(
            ProgressCompleted * 100d / ProgressTotal.Value,
            0d,
            100d),
        0 => 100d,
        _ => 0d,
    };

    public string ProgressDisplay => ProgressTotal.HasValue
        ? $"{ProgressCompleted:N0} / {ProgressTotal.Value:N0} " +
          $"({ProgressPercentage:0}%)"
        : "Preparing scan…";

    public string TargetDisplay => _currentSession is null
        ? "No monitoring target"
        : $"{_currentSession.Identity.ProcessName} " +
          $"(PID {_currentSession.Identity.ProcessId}, " +
          $"{_currentSession.Identity.Architecture})";

    public string ActiveCandidateDisplay =>
        $"{PipelineState?.ActiveRound.CandidateCount ?? 0:N0}";

    public string PendingCandidateDisplay =>
        PipelineState?.PendingResult is null
            ? "—"
            : $"{PipelineState.PendingResult.AfterCount:N0}";

    public string RemovedCandidateDisplay =>
        PipelineState?.PendingResult is null
            ? "—"
            : $"{PipelineState.PendingResult.BeforeCount -
                 PipelineState.PendingResult.AfterCount:N0}";

    public string EstimateDisplay => UnknownEstimate is null
        ? "Run Estimate before capturing an unknown baseline."
        : $"{UnknownEstimate.CandidateCount:N0} candidates · " +
          $"{FormatBytes(UnknownEstimate.ScannableBytes)} readable · " +
          $"{FormatBytes(UnknownEstimate.EstimatedDiskBytes)} estimated disk";

    public SnapshotDescriptor? CurrentSnapshot =>
        PipelineState?.PendingResult?.Round.Snapshot ??
        PipelineState?.ActiveRound.Snapshot;

    public AsyncRelayCommand EstimateUnknownCommand { get; }

    public AsyncRelayCommand FirstScanCommand { get; }

    public AsyncRelayCommand NextScanCommand { get; }

    public AsyncRelayCommand KeepCommand { get; }

    public AsyncRelayCommand DiscardCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand NewScanCommand { get; }

    public RelayCommand ViewResultsCommand { get; }

    public Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UpdateReadyStatus();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _sessionService.SessionChanged -= OnSessionChanged;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _disposed = true;
    }

    private async Task EstimateUnknownAsync()
    {
        BeginOperation("Estimating unknown initial scan…");

        try
        {
            var result = await _workflow.EstimateUnknownAsync(
                SelectedValueType,
                SelectedAlignmentMode,
                _operationCancellation!.Token);

            if (result.IsFailure)
            {
                HandleFailure(result.Error);
                return;
            }

            UnknownEstimate = result.Value;
            StatusMessage =
                $"Estimate ready: {result.Value.CandidateCount:N0} candidates.";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task FirstScanAsync()
    {
        if (IsUnknownFirstScan)
        {
            await StartUnknownAsync();
            return;
        }

        var request = CreateRequest(ScanComparisonMode.ExactValue);

        if (request.IsFailure)
        {
            HandleFailure(request.Error);
            return;
        }

        BeginOperation("Running exact First Scan…");

        try
        {
            var result = await _workflow.StartExactAsync(
                request.Value,
                CreateProgress(),
                _operationCancellation!.Token);

            if (result.IsFailure)
            {
                HandleFailure(result.Error);
                return;
            }

            ApplyStartedWorkflow(result.Value);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task StartUnknownAsync()
    {
        if (UnknownEstimate is null)
        {
            await EstimateUnknownAsync();

            if (UnknownEstimate is null)
            {
                return;
            }
        }

        if (UnknownEstimate.RequiresDiskBackedStorage &&
            !_confirmation.Confirm(
                "Unknown Initial Scan",
                $"Capture {UnknownEstimate.CandidateCount:N0} candidates " +
                $"using approximately " +
                $"{FormatBytes(UnknownEstimate.EstimatedDiskBytes)} " +
                "of temporary storage?"))
        {
            StatusMessage = "Unknown Initial Scan was not started.";
            return;
        }

        BeginOperation("Capturing unknown initial values…");

        try
        {
            var result = await _workflow.StartUnknownAsync(
                SelectedValueType,
                SelectedAlignmentMode,
                CreateProgress(),
                _operationCancellation!.Token);

            if (result.IsFailure)
            {
                HandleFailure(result.Error);
                return;
            }

            ApplyStartedWorkflow(result.Value);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task NextScanAsync()
    {
        var request = CreateRequest(SelectedComparisonMode);

        if (request.IsFailure)
        {
            HandleFailure(request.Error);
            return;
        }

        BeginOperation($"Running {SelectedComparisonMode} Next Scan…");

        try
        {
            var result = await _workflow.RunNextAsync(
                request.Value,
                CreateProgress(),
                _operationCancellation!.Token);

            if (result.IsFailure)
            {
                HandleFailure(result.Error);
                return;
            }

            PipelineState = _workflow.CurrentState;
            WarningMessage = result.Value.Summary.IsPartial
                ? $"Next Scan completed with " +
                  $"{result.Value.Summary.WarningCount:N0} warning(s)."
                : null;
            StatusMessage =
                $"Next Scan found {result.Value.AfterCount:N0} candidates. " +
                "Keep or Discard the pending result.";
            PublishSnapshot(result.Value.Round.Snapshot);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task KeepAsync()
    {
        BeginOperation("Keeping pending scan result…");

        try
        {
            var result = await _workflow.KeepAsync(
                _operationCancellation!.Token);

            if (result.IsFailure)
            {
                HandleFailure(result.Error);
                return;
            }

            PipelineState = result.Value;
            StatusMessage =
                $"Kept {result.Value.ActiveRound.CandidateCount:N0} candidates.";
            PublishSnapshot(result.Value.ActiveRound.Snapshot);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task DiscardAsync()
    {
        BeginOperation("Discarding pending scan result…");

        try
        {
            var result = await _workflow.DiscardAsync(
                _operationCancellation!.Token);

            if (result.IsFailure)
            {
                HandleFailure(result.Error);
                return;
            }

            PipelineState = result.Value;
            StatusMessage = "Pending scan result was discarded.";
            PublishSnapshot(result.Value.ActiveRound.Snapshot);
        }
        finally
        {
            EndOperation();
        }
    }

    private Result<ScanRequest> CreateRequest(
        ScanComparisonMode comparisonMode)
    {
        ScanValue? value = null;

        if (RequiresValue(comparisonMode))
        {
            var parsed = _parser.Parse(
                ValueText,
                SelectedValueType);

            if (parsed.IsFailure)
            {
                return Result<ScanRequest>.Failure(parsed.Error);
            }

            value = parsed.Value;
        }

        var tolerance = 0d;

        if (UsesFloatingPointTolerance &&
            (!double.TryParse(
                 ToleranceText,
                 NumberStyles.Float,
                 CultureInfo.InvariantCulture,
                 out tolerance) ||
             !double.IsFinite(tolerance) ||
             tolerance < 0))
        {
            return Result<ScanRequest>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Floating-point tolerance must be finite and non-negative."));
        }

        return ScanRequest.Create(
            SelectedValueType,
            comparisonMode,
            value,
            SelectedAlignmentMode,
            tolerance,
            MaximumResults);
    }

    private void ApplyStartedWorkflow(ScanWorkflowStartResult result)
    {
        PipelineState = result.PipelineState;
        WarningMessage = result.IsPartial
            ? string.Join(
                " · ",
                result.Warnings.Select(
                    warning => warning.ToDisplayMessage()))
            : null;
        StatusMessage =
            $"First Scan created {result.Snapshot.RecordCount:N0} candidates.";
        PublishSnapshot(result.Snapshot);
    }

    private void BeginOperation(string status)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        ProgressCompleted = 0;
        ProgressTotal = null;
        ProgressStage = status;
        ErrorMessage = null;
        WarningMessage = null;
        StatusMessage = status;
        IsBusy = true;
    }

    private void EndOperation()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        IsBusy = false;
    }

    private IProgress<OperationProgress> CreateProgress() =>
        new Progress<OperationProgress>(
            progress =>
            {
                ProgressCompleted = progress.Completed;
                ProgressTotal = progress.Total;
                ProgressStage =
                    progress.Stage ?? ProgressStage;
            });

    private void Cancel()
    {
        _operationCancellation?.Cancel();
        StatusMessage = "Cancelling scan…";
    }

    private void NewScan()
    {
        if (HasActiveScan &&
            !_confirmation.Confirm(
                "New Scan",
                "Start a new scan root for this monitoring session? " +
                "Existing temporary snapshots will remain available " +
                "to Temporary Manager."))
        {
            return;
        }

        PipelineState = null;
        UnknownEstimate = null;
        WarningMessage = null;
        ErrorMessage = null;
        StatusMessage = "Configure and start a new First Scan.";
    }

    private void ViewResults()
    {
        if (CurrentSnapshot is not null)
        {
            PublishSnapshot(CurrentSnapshot);
        }
    }

    private void PublishSnapshot(SnapshotDescriptor snapshot)
    {
        SnapshotReady?.Invoke(
            this,
            new ScanSnapshotReadyEventArgs(snapshot));
    }

    private void HandleFailure(Error error)
    {
        if (error.Code == ErrorCode.Cancelled)
        {
            StatusMessage = error.ToDisplayMessage();
            return;
        }

        ErrorMessage = error.ToDisplayMessage();
        StatusMessage = "The scan operation did not complete.";
        _ = _logger.Log(
            AppLogLevel.Error,
            ErrorMessage,
            error.Exception);
    }

    private void OnSessionChanged(
        object? sender,
        MonitoringSessionChangedEventArgs eventArgs)
    {
        if (_synchronizationContext is not null &&
            SynchronizationContext.Current != _synchronizationContext)
        {
            _synchronizationContext.Post(
                _ => ApplySession(eventArgs.Session),
                null);
            return;
        }

        ApplySession(eventArgs.Session);
    }

    private void ApplySession(MonitoringSession session)
    {
        var changed =
            _currentSession?.SessionId != session.SessionId;
        _currentSession = session;

        if (changed ||
            session.State != MonitoringSessionState.Connected)
        {
            _operationCancellation?.Cancel();
            PipelineState = null;
            UnknownEstimate = null;
        }

        OnPropertyChanged(nameof(IsSessionConnected));
        OnPropertyChanged(nameof(TargetDisplay));
        UpdateReadyStatus();
        NotifyState();
    }

    private void UpdateReadyStatus()
    {
        if (!IsSessionConnected)
        {
            StatusMessage =
                "Start monitoring a process before scanning memory.";
        }
        else if (!HasActiveScan && !IsBusy)
        {
            StatusMessage =
                "Configure Exact Value or Unknown Initial Value, then start First Scan.";
        }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsUnknownFirstScan));
        OnPropertyChanged(nameof(CanEstimateUnknown));
        OnPropertyChanged(nameof(CanFirstScan));
        OnPropertyChanged(nameof(CanNextScan));
        OnPropertyChanged(nameof(CanKeep));
        OnPropertyChanged(nameof(CanDiscard));
        OnPropertyChanged(nameof(CanConfigureFirstScan));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        EstimateUnknownCommand.NotifyCanExecuteChanged();
        FirstScanCommand.NotifyCanExecuteChanged();
        NextScanCommand.NotifyCanExecuteChanged();
        KeepCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        NewScanCommand.NotifyCanExecuteChanged();
        ViewResultsCommand.NotifyCanExecuteChanged();
    }

    private static bool RequiresValue(ScanComparisonMode mode) =>
        mode is
            ScanComparisonMode.ExactValue or
            ScanComparisonMode.GreaterThan or
            ScanComparisonMode.LessThan;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes:N0} B"
            : $"{value:N2} {units[unit]}";
    }
}

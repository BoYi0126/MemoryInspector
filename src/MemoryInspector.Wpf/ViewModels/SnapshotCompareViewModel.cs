using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.Snapshots.Comparison;
using MemoryInspector.Common;
using MemoryInspector.Wpf.Mvvm;
using MemoryInspector.Wpf.Services;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class SnapshotCompareViewModel :
    ObservableObject,
    IDisposable
{
    public const int DifferencePageSize = 500;

    private readonly object _operationSync = new();
    private readonly IFilterPipelineService _pipeline;
    private readonly ISnapshotCompareService _compareService;
    private readonly ISnapshotComparisonExportService _exportService;
    private readonly ISnapshotCompareFileDialogService _fileDialogService;
    private readonly IAppLogger _logger;
    private IReadOnlyList<SnapshotCompareNodeOption> _nodes = [];
    private IReadOnlyList<SnapshotDifferenceRowViewModel> _rows = [];
    private SnapshotCompareNodeOption? _selectedLeft;
    private SnapshotCompareNodeOption? _selectedRight;
    private SnapshotComparisonSummary? _summary;
    private CancellationTokenSource? _operationCancellation;
    private long _pageNumber = 1;
    private long _totalPages;
    private long _totalCount;
    private double _progressPercentage;
    private bool _isBusy;
    private string _statusMessage =
        "Refresh scan nodes to compare two snapshots.";
    private string? _errorMessage;
    private bool _disposed;

    public SnapshotCompareViewModel(
        IFilterPipelineService pipeline,
        ISnapshotCompareService compareService,
        ISnapshotComparisonExportService exportService,
        ISnapshotCompareFileDialogService fileDialogService,
        IAppLogger logger)
    {
        _pipeline = Guard.NotNull(pipeline);
        _compareService = Guard.NotNull(compareService);
        _exportService = Guard.NotNull(exportService);
        _fileDialogService = Guard.NotNull(fileDialogService);
        _logger = Guard.NotNull(logger);
        RefreshNodesCommand = new RelayCommand(
            RefreshNodes,
            () => !IsBusy);
        CompareCommand = new AsyncRelayCommand(
            () => ComparePageAsync(1),
            () => CanCompare);
        FirstPageCommand = new AsyncRelayCommand(
            () => ComparePageAsync(1),
            () => CanGoToPreviousPage);
        PreviousPageCommand = new AsyncRelayCommand(
            () => ComparePageAsync(PageNumber - 1),
            () => CanGoToPreviousPage);
        NextPageCommand = new AsyncRelayCommand(
            () => ComparePageAsync(PageNumber + 1),
            () => CanGoToNextPage);
        LastPageCommand = new AsyncRelayCommand(
            () => ComparePageAsync(TotalPages),
            () => CanGoToNextPage);
        ExportCommand = new AsyncRelayCommand(
            () => ExportAsync(),
            () => CanExport);
    }

    public IReadOnlyList<SnapshotCompareNodeOption> Nodes
    {
        get => _nodes;
        private set => SetProperty(ref _nodes, value);
    }

    public IReadOnlyList<SnapshotDifferenceRowViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public SnapshotCompareNodeOption? SelectedLeft
    {
        get => _selectedLeft;
        set
        {
            if (SetProperty(ref _selectedLeft, value))
            {
                SelectionChanged();
            }
        }
    }

    public SnapshotCompareNodeOption? SelectedRight
    {
        get => _selectedRight;
        set
        {
            if (SetProperty(ref _selectedRight, value))
            {
                SelectionChanged();
            }
        }
    }

    public SnapshotComparisonSummary? Summary
    {
        get => _summary;
        private set
        {
            if (SetProperty(ref _summary, value))
            {
                NotifySummaryProperties();
                NotifyCommands();
            }
        }
    }

    public long PageNumber
    {
        get => _pageNumber;
        private set
        {
            if (SetProperty(ref _pageNumber, value))
            {
                OnPropertyChanged(nameof(PageDisplay));
                NotifyCommands();
            }
        }
    }

    public long TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(PageDisplay));
                NotifyCommands();
            }
        }
    }

    public long TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(ResultCountDisplay));
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

    public double ProgressPercentage
    {
        get => _progressPercentage;
        private set =>
            SetProperty(ref _progressPercentage, value);
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

    public bool CanCompare =>
        !IsBusy &&
        SelectedLeft is not null &&
        SelectedRight is not null &&
        SelectedLeft.RoundId != SelectedRight.RoundId;

    public bool CanExport =>
        !IsBusy &&
        Summary is not null &&
        SelectedLeft?.Snapshot == Summary.Left &&
        SelectedRight?.Snapshot == Summary.Right;

    public bool CanGoToPreviousPage =>
        !IsBusy && Summary is not null && PageNumber > 1;

    public bool CanGoToNextPage =>
        !IsBusy &&
        Summary is not null &&
        PageNumber < TotalPages;

    public string PageDisplay => TotalPages == 0
        ? "Page 0 of 0"
        : $"Page {PageNumber:N0} of {TotalPages:N0}";

    public string ResultCountDisplay =>
        $"{TotalCount:N0} compared addresses";

    public string AddedDisplay =>
        $"{Summary?.AddedCount ?? 0:N0}";

    public string RemovedDisplay =>
        $"{Summary?.RemovedCount ?? 0:N0}";

    public string ChangedDisplay =>
        $"{Summary?.ChangedCount ?? 0:N0}";

    public string UnchangedDisplay =>
        $"{Summary?.UnchangedCount ?? 0:N0}";

    public string CountDifferenceDisplay =>
        FormatSigned(Summary?.CountDifference);

    public string StorageDifferenceDisplay =>
        Summary is null
            ? "—"
            : FormatSignedBytes(
                Summary.StorageSizeDifference);

    public RelayCommand RefreshNodesCommand { get; }

    public AsyncRelayCommand CompareCommand { get; }

    public AsyncRelayCommand FirstPageCommand { get; }

    public AsyncRelayCommand PreviousPageCommand { get; }

    public AsyncRelayCommand NextPageCommand { get; }

    public AsyncRelayCommand LastPageCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RefreshNodes();
        return Task.CompletedTask;
    }

    public async Task ComparePageAsync(
        long pageNumber,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var left = SelectedLeft;
        var right = SelectedRight;

        if (left is null ||
            right is null ||
            left.RoundId == right.RoundId)
        {
            ErrorMessage =
                "Select two different scan nodes.";
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        ErrorMessage = null;
        ProgressPercentage = 0;
        StatusMessage =
            $"Comparing node #{left.Round.RoundNumber:N0} with " +
            $"node #{right.Round.RoundNumber:N0}…";
        var progress = new Progress<OperationProgress>(
            value =>
            {
                ProgressPercentage =
                    value.Percentage ?? 0;
                StatusMessage = value.Stage ??
                    StatusMessage;
            });

        try
        {
            var result = await _compareService.CompareAsync(
                left.Snapshot,
                right.Snapshot,
                pageNumber,
                DifferencePageSize,
                progress,
                operation.Token);

            if (!IsCurrentOperation(operation) ||
                operation.IsCancellationRequested)
            {
                return;
            }

            if (result.IsFailure)
            {
                ErrorMessage = result.Error.ToDisplayMessage();
                StatusMessage = ErrorMessage;
                LogFailure(result.Error);
                return;
            }

            Summary = result.Value.Summary;
            Rows = Array.AsReadOnly(
                result.Value.Differences.Items
                    .Select(item =>
                        new SnapshotDifferenceRowViewModel(item))
                    .ToArray());
            PageNumber = result.Value.Differences.PageNumber;
            TotalPages = result.Value.Differences.TotalPages;
            TotalCount = result.Value.Differences.TotalCount;
            ProgressPercentage = 100;
            StatusMessage =
                $"Compared {TotalCount:N0} addresses; " +
                $"{Summary.TotalDifferenceCount:N0} differ.";
        }
        catch (OperationCanceledException)
            when (operation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            ErrorMessage = "Snapshot comparison failed.";
            StatusMessage = ErrorMessage;
            _ = _logger.Log(
                AppLogLevel.Error,
                ErrorMessage,
                exception);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    public async Task ExportAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var left = SelectedLeft;
        var right = SelectedRight;

        if (!CanExport ||
            left is null ||
            right is null)
        {
            ErrorMessage =
                "Run a comparison before exporting.";
            return;
        }

        var path = _fileDialogService.SelectComparisonExportFile(
            $"snapshot-compare-{left.Snapshot.NodeId}-" +
            $"{right.Snapshot.NodeId}.csv");

        if (path is null)
        {
            StatusMessage = "Comparison export was cancelled.";
            return;
        }

        var operation = BeginOperation(cancellationToken);
        IsBusy = true;
        ErrorMessage = null;
        ProgressPercentage = 0;
        StatusMessage = "Exporting snapshot comparison…";
        var progress = new Progress<OperationProgress>(
            value =>
            {
                ProgressPercentage =
                    value.Percentage ?? 0;
                StatusMessage = value.Stage ??
                    StatusMessage;
            });

        try
        {
            var result = await _exportService.ExportCsvAsync(
                path,
                left.Snapshot,
                right.Snapshot,
                progress,
                operation.Token);

            if (!IsCurrentOperation(operation) ||
                operation.IsCancellationRequested)
            {
                return;
            }

            if (result.IsFailure)
            {
                ErrorMessage = result.Error.ToDisplayMessage();
                StatusMessage = ErrorMessage;
                LogFailure(result.Error);
                return;
            }

            ProgressPercentage = 100;
            StatusMessage =
                $"Exported comparison to {path}.";
        }
        catch (OperationCanceledException)
            when (operation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            ErrorMessage = "Snapshot comparison export failed.";
            StatusMessage = ErrorMessage;
            _ = _logger.Log(
                AppLogLevel.Error,
                ErrorMessage,
                exception);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_operationSync)
        {
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }

        _disposed = true;
    }

    private void RefreshNodes()
    {
        var leftId = SelectedLeft?.RoundId;
        var rightId = SelectedRight?.RoundId;
        var rounds = _pipeline.CurrentState?.Rounds
            .OrderBy(round => round.RoundNumber)
            .Select(round =>
                new SnapshotCompareNodeOption(round))
            .ToArray() ?? [];
        Nodes = Array.AsReadOnly(rounds);
        SelectedLeft = Nodes.FirstOrDefault(node =>
                node.RoundId == leftId) ??
            Nodes.FirstOrDefault();
        SelectedRight = Nodes.FirstOrDefault(node =>
                node.RoundId == rightId) ??
            Nodes.Skip(1).FirstOrDefault() ??
            Nodes.FirstOrDefault();
        StatusMessage = Nodes.Count >= 2
            ? $"Loaded {Nodes.Count:N0} scan nodes."
            : "At least two scan nodes are required.";
        ErrorMessage = null;
        NotifyCommands();
    }

    private void SelectionChanged()
    {
        CancelOperation();
        Summary = null;
        Rows = [];
        PageNumber = 1;
        TotalPages = 0;
        TotalCount = 0;
        ProgressPercentage = 0;
        ErrorMessage = null;
        NotifyCommands();
    }

    private CancellationTokenSource BeginOperation(
        CancellationToken cancellationToken)
    {
        lock (_operationSync)
        {
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            return _operationCancellation;
        }
    }

    private void EndOperation(CancellationTokenSource operation)
    {
        if (IsCurrentOperation(operation))
        {
            lock (_operationSync)
            {
                if (ReferenceEquals(
                    _operationCancellation,
                    operation))
                {
                    _operationCancellation = null;
                }
            }

            operation.Dispose();
            IsBusy = false;
        }
    }

    private void CancelOperation()
    {
        lock (_operationSync)
        {
            _operationCancellation?.Cancel();
        }
    }

    private bool IsCurrentOperation(
        CancellationTokenSource operation)
    {
        lock (_operationSync)
        {
            return ReferenceEquals(
                _operationCancellation,
                operation);
        }
    }

    private void NotifyCommands()
    {
        RefreshNodesCommand.NotifyCanExecuteChanged();
        CompareCommand.NotifyCanExecuteChanged();
        FirstPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCompare));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
    }

    private void NotifySummaryProperties()
    {
        OnPropertyChanged(nameof(AddedDisplay));
        OnPropertyChanged(nameof(RemovedDisplay));
        OnPropertyChanged(nameof(ChangedDisplay));
        OnPropertyChanged(nameof(UnchangedDisplay));
        OnPropertyChanged(nameof(CountDifferenceDisplay));
        OnPropertyChanged(nameof(StorageDifferenceDisplay));
    }

    private void LogFailure(Error error)
    {
        if (error.Code != ErrorCode.Cancelled)
        {
            _ = _logger.Log(
                AppLogLevel.Error,
                error.ToDisplayMessage(),
                error.Exception);
        }
    }

    private static string FormatSigned(long? value)
    {
        return value switch
        {
            null => "—",
            > 0 => $"+{value.Value:N0}",
            _ => $"{value.Value:N0}",
        };
    }

    private static string FormatSignedBytes(long bytes)
    {
        var sign = bytes > 0 ? "+" : bytes < 0 ? "-" : string.Empty;
        var magnitude = bytes == long.MinValue
            ? (ulong)long.MaxValue + 1
            : (ulong)Math.Abs(bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var display = (double)magnitude;
        var unit = 0;

        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{sign}{magnitude:N0} {units[unit]}"
            : $"{sign}{display:N2} {units[unit]}";
    }
}

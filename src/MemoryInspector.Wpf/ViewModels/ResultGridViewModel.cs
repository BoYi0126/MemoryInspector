using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.Results;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Wpf.Mvvm;
using MemoryInspector.Wpf.Services;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class ResultGridViewModel :
    ObservableObject,
    IDisposable
{
    private readonly object _loadSync = new();
    private readonly IResultGridService _resultGridService;
    private readonly IFilterPipelineService? _pipelineService;
    private readonly IClipboardService _clipboardService;
    private readonly IAppLogger _logger;
    private IReadOnlyList<ResultGridRowViewModel> _loadedRows = [];
    private IReadOnlyList<ResultGridRowViewModel> _rows = [];
    private CancellationTokenSource? _loadCancellation;
    private SnapshotDescriptor? _snapshot;
    private ResultGridRowViewModel? _selectedRow;
    private ResultGridSortOption _selectedSortOption =
        ResultGridSortOption.Address;
    private bool _sortDescending;
    private bool _isBusy;
    private long _pageNumber = 1;
    private long _totalPages;
    private long _totalCount;
    private int _pageSize =
        SnapshotCachePolicy.DefaultPageSize;
    private string _statusMessage =
        "Load an active scan snapshot to view its candidates.";
    private string? _errorMessage;
    private bool _disposed;

    public ResultGridViewModel(
        IResultGridService resultGridService,
        IFilterPipelineService pipelineService,
        IClipboardService clipboardService,
        IAppLogger logger)
        : this(
            resultGridService,
            clipboardService,
            logger,
            Guard.NotNull(pipelineService))
    {
    }

    public ResultGridViewModel(
        IResultGridService resultGridService,
        IClipboardService clipboardService,
        IAppLogger logger)
        : this(
            resultGridService,
            clipboardService,
            logger,
            pipelineService: null)
    {
    }

    private ResultGridViewModel(
        IResultGridService resultGridService,
        IClipboardService clipboardService,
        IAppLogger logger,
        IFilterPipelineService? pipelineService)
    {
        _resultGridService = Guard.NotNull(resultGridService);
        _pipelineService = pipelineService;
        _clipboardService = Guard.NotNull(clipboardService);
        _logger = Guard.NotNull(logger);

        LoadActiveResultsCommand = new AsyncRelayCommand(
            () => LoadActiveResultsAsync(
                CancellationToken.None));
        ReloadCommand = new AsyncRelayCommand(
            ReloadAsync,
            () => HasSnapshot,
            allowConcurrentExecutions: true);
        FirstPageCommand = new AsyncRelayCommand(
            () => LoadPageAsync(1),
            () => CanGoToPreviousPage,
            allowConcurrentExecutions: true);
        PreviousPageCommand = new AsyncRelayCommand(
            () => LoadPageAsync(Math.Max(1, PageNumber - 1)),
            () => CanGoToPreviousPage,
            allowConcurrentExecutions: true);
        NextPageCommand = new AsyncRelayCommand(
            () => LoadPageAsync(
                Math.Min(TotalPages, PageNumber + 1)),
            () => CanGoToNextPage,
            allowConcurrentExecutions: true);
        LastPageCommand = new AsyncRelayCommand(
            () => LoadPageAsync(TotalPages),
            () => CanGoToNextPage,
            allowConcurrentExecutions: true);
        CopyAddressCommand = new RelayCommand(
            CopySelectedAddress,
            () => SelectedRow is not null);
        AddToWatchCommand = new RelayCommand(
            RequestAddToWatch,
            () => SelectedRow is not null);
        SaveAddressCommand = new RelayCommand(
            RequestSaveAddress,
            () => SelectedRow is not null);
        EditValueCommand = new RelayCommand(
            RequestEditValue,
            () => SelectedRow is not null);
        OpenHexCommand = new RelayCommand(
            RequestOpenHex,
            () => SelectedRow is not null);
    }

    public event EventHandler<ResultAddressActionRequestedEventArgs>?
        AddToWatchRequested;

    public event EventHandler<ResultAddressActionRequestedEventArgs>?
        SaveAddressRequested;

    public event EventHandler<MemoryEditRequestedEventArgs>?
        EditValueRequested;

    public event EventHandler<HexViewerRequestedEventArgs>?
        OpenHexRequested;

    public IReadOnlyList<ResultGridSortOption> SortOptions { get; } =
        Enum.GetValues<ResultGridSortOption>();

    public IReadOnlyList<ResultGridRowViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public ResultGridRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                NotifyAddressCommands();
            }
        }
    }

    public ResultGridSortOption SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(ref _selectedSortOption, value))
            {
                RebuildCurrentPage();
            }
        }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (SetProperty(ref _sortDescending, value))
            {
                RebuildCurrentPage();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public long PageNumber
    {
        get => _pageNumber;
        private set
        {
            if (SetProperty(ref _pageNumber, value))
            {
                OnPropertyChanged(nameof(PageDisplay));
                NotifyPageCommands();
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
                NotifyPageCommands();
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
                OnPropertyChanged(nameof(CandidateCountDisplay));
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        private set => SetProperty(ref _pageSize, value);
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

    public bool HasSnapshot => _snapshot is not null;

    public bool CanGoToPreviousPage =>
        HasSnapshot && PageNumber > 1;

    public bool CanGoToNextPage =>
        HasSnapshot &&
        TotalPages > 0 &&
        PageNumber < TotalPages;

    public string PageDisplay => TotalPages == 0
        ? "Page 0 of 0"
        : $"Page {PageNumber:N0} of {TotalPages:N0}";

    public string CandidateCountDisplay =>
        $"{TotalCount:N0} candidates";

    public string SnapshotDisplay => _snapshot is null
        ? "No snapshot selected"
        : $"Node {_snapshot.NodeId:N0} · " +
          $"{_snapshot.ValueType} · " +
          $"{_snapshot.StorageKind}";

    public AsyncRelayCommand LoadActiveResultsCommand { get; }

    public AsyncRelayCommand ReloadCommand { get; }

    public AsyncRelayCommand FirstPageCommand { get; }

    public AsyncRelayCommand PreviousPageCommand { get; }

    public AsyncRelayCommand NextPageCommand { get; }

    public AsyncRelayCommand LastPageCommand { get; }

    public RelayCommand CopyAddressCommand { get; }

    public RelayCommand AddToWatchCommand { get; }

    public RelayCommand SaveAddressCommand { get; }

    public RelayCommand EditValueCommand { get; }

    public RelayCommand OpenHexCommand { get; }

    public async Task InitializeAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Guard.NotNull(settings);
        PageSize = settings.PageSize;

        if (_pipelineService?.CurrentState is not null)
        {
            await LoadActiveResultsAsync(cancellationToken);
        }
    }

    public Task ShowSnapshotAsync(
        SnapshotDescriptor snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var changed = _snapshot is null ||
                      _snapshot.SessionId != snapshot.SessionId ||
                      _snapshot.NodeId != snapshot.NodeId ||
                      !_snapshot.Checksum.Equals(
                          snapshot.Checksum,
                          StringComparison.OrdinalIgnoreCase);
        _snapshot = snapshot;

        if (changed)
        {
            _loadedRows = [];
            Rows = [];
            SelectedRow = null;
            PageNumber = 1;
            TotalCount = snapshot.RecordCount;
            TotalPages = snapshot.RecordCount == 0
                ? 0
                : snapshot.RecordCount / PageSize +
                  (snapshot.RecordCount % PageSize == 0
                      ? 0
                      : 1);
        }

        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(SnapshotDisplay));
        ReloadCommand.NotifyCanExecuteChanged();
        NotifyPageCommands();
        return LoadPageAsync(1, cancellationToken);
    }

    public async Task LoadActiveResultsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var state = _pipelineService?.CurrentState;

        if (state is null)
        {
            StatusMessage =
                "The scan pipeline does not have an active result.";
            ErrorMessage = null;
            return;
        }

        var snapshot = state.PendingResult?.Round.Snapshot ??
            state.ActiveRound.Snapshot;
        await ShowSnapshotAsync(snapshot, cancellationToken);
    }

    public async Task LoadPageAsync(
        long pageNumber,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_snapshot is null)
        {
            StatusMessage = "Select a snapshot before loading results.";
            return;
        }

        CancellationTokenSource currentCancellation;

        lock (_loadSync)
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            currentCancellation = _loadCancellation;
        }

        var requestedSnapshot = _snapshot;
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = $"Loading page {pageNumber:N0}…";

        try
        {
            var result = await _resultGridService.LoadPageAsync(
                requestedSnapshot,
                pageNumber,
                PageSize,
                currentCancellation.Token);

            if (currentCancellation.IsCancellationRequested ||
                !IsCurrentLoad(currentCancellation) ||
                !ReferenceEquals(requestedSnapshot, _snapshot))
            {
                return;
            }

            if (result.IsFailure)
            {
                if (result.Error.Code != ErrorCode.Cancelled)
                {
                    ErrorMessage = result.Error.ToDisplayMessage();
                    StatusMessage = "Result page could not be loaded.";
                    _ = _logger.Log(
                        AppLogLevel.Error,
                        ErrorMessage,
                        result.Error.Exception);
                }

                return;
            }

            var previousAddress = SelectedRow?.Address;
            _loadedRows = result.Value.Items
                .Select(item => new ResultGridRowViewModel(item))
                .ToArray();
            PageNumber = result.Value.PageNumber;
            TotalPages = result.Value.TotalPages;
            TotalCount = result.Value.TotalCount;
            RebuildCurrentPage();
            SelectedRow = previousAddress.HasValue
                ? Rows.FirstOrDefault(row =>
                    row.Address == previousAddress.Value)
                : null;
            StatusMessage =
                $"Loaded {Rows.Count:N0} candidates on the current page.";
        }
        catch (OperationCanceledException)
            when (currentCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = "Result page could not be loaded.";
            StatusMessage = ErrorMessage;
            _ = _logger.Log(
                AppLogLevel.Error,
                ErrorMessage,
                exception);
        }
        finally
        {
            if (IsCurrentLoad(currentCancellation))
            {
                lock (_loadSync)
                {
                    if (ReferenceEquals(
                        _loadCancellation,
                        currentCancellation))
                    {
                        _loadCancellation = null;
                    }
                }

                currentCancellation.Dispose();
                IsBusy = false;
            }
        }
    }

    public void ApplyMemoryWriteResult(
        MemoryWriteRequest request,
        MemoryWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        if (!result.Success ||
            !result.ReadBackValue.HasValue ||
            !_loadedRows.Any(row =>
                row.Address == request.Address &&
                row.ValueType == request.ValueType))
        {
            return;
        }

        _loadedRows = _loadedRows
            .Select(row =>
                row.Address == request.Address &&
                row.ValueType == request.ValueType
                    ? new ResultGridRowViewModel(
                        new ResultGridItem(
                            request.Address,
                            request.ValueType,
                            result.ReadBackValue.Value.Span,
                            ResultReadStatus.Available))
                    : row)
            .ToArray();
        RebuildCurrentPage();
        SelectedRow = Rows.FirstOrDefault(row =>
            row.Address == request.Address &&
            row.ValueType == request.ValueType);
        StatusMessage =
            $"{SelectedRow?.AddressDisplay ?? "Result row"} " +
            "was refreshed after a memory write.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_loadSync)
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
        }

        _disposed = true;
    }

    private Task ReloadAsync()
    {
        return LoadPageAsync(PageNumber);
    }

    private void RebuildCurrentPage()
    {
        var selectedAddress = SelectedRow?.Address;
        IEnumerable<ResultGridRowViewModel> query =
            SelectedSortOption switch
            {
                ResultGridSortOption.Address =>
                    _loadedRows.OrderBy(row => row.Address),
                ResultGridSortOption.Value =>
                    _loadedRows.OrderBy(
                        row => row,
                        ResultValueComparer.Instance),
                ResultGridSortOption.ReadStatus =>
                    _loadedRows
                        .OrderBy(row => row.ReadStatus)
                        .ThenBy(row => row.Address),
                _ => throw new ArgumentOutOfRangeException(),
            };

        if (SortDescending)
        {
            query = query.Reverse();
        }

        Rows = Array.AsReadOnly(query.ToArray());
        SelectedRow = selectedAddress.HasValue
            ? Rows.FirstOrDefault(row =>
                row.Address == selectedAddress.Value)
            : null;
    }

    private void CopySelectedAddress()
    {
        if (SelectedRow is null)
        {
            return;
        }

        var result = _clipboardService.SetText(
            SelectedRow.AddressDisplay);
        StatusMessage = result.IsSuccess
            ? $"Copied {SelectedRow.AddressDisplay}."
            : result.Error.ToDisplayMessage();
        ErrorMessage = result.IsFailure
            ? result.Error.ToDisplayMessage()
            : null;
    }

    private void RequestAddToWatch()
    {
        if (SelectedRow is null)
        {
            return;
        }

        AddToWatchRequested?.Invoke(
            this,
            new ResultAddressActionRequestedEventArgs(
                SelectedRow));
        StatusMessage =
            $"{SelectedRow.AddressDisplay} was sent to Watch.";
    }

    private void RequestSaveAddress()
    {
        if (SelectedRow is null)
        {
            return;
        }

        SaveAddressRequested?.Invoke(
            this,
            new ResultAddressActionRequestedEventArgs(
                SelectedRow));
        StatusMessage =
            $"{SelectedRow.AddressDisplay} was sent to Saved Addresses.";
    }

    private void RequestEditValue()
    {
        if (SelectedRow is null)
        {
            return;
        }

        EditValueRequested?.Invoke(
            this,
            new MemoryEditRequestedEventArgs(
                SelectedRow.Address,
                SelectedRow.ValueType,
                MemoryWriteSource.ScanResult));
        StatusMessage =
            $"{SelectedRow.AddressDisplay} was sent to Memory Editor.";
    }

    private void RequestOpenHex()
    {
        if (SelectedRow is null)
        {
            return;
        }

        OpenHexRequested?.Invoke(
            this,
            new HexViewerRequestedEventArgs(
                SelectedRow.Address));
        StatusMessage =
            $"{SelectedRow.AddressDisplay} was sent to Hex Viewer.";
    }

    private bool IsCurrentLoad(
        CancellationTokenSource cancellation)
    {
        lock (_loadSync)
        {
            return ReferenceEquals(
                _loadCancellation,
                cancellation);
        }
    }

    private void NotifyAddressCommands()
    {
        CopyAddressCommand.NotifyCanExecuteChanged();
        AddToWatchCommand.NotifyCanExecuteChanged();
        SaveAddressCommand.NotifyCanExecuteChanged();
        EditValueCommand.NotifyCanExecuteChanged();
        OpenHexCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPageCommands()
    {
        FirstPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
    }

    private sealed class ResultValueComparer :
        IComparer<ResultGridRowViewModel>
    {
        public static ResultValueComparer Instance { get; } =
            new();

        public int Compare(
            ResultGridRowViewModel? x,
            ResultGridRowViewModel? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var comparison =
                ResultGridRowViewModel.CompareValues(x, y);
            return comparison != 0
                ? comparison
                : x.Address.CompareTo(y.Address);
        }
    }
}

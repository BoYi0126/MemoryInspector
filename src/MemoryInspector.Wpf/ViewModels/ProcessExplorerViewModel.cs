using System.Globalization;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Processes;
using MemoryInspector.Common;
using MemoryInspector.Core.Processes;
using MemoryInspector.Wpf.Mvvm;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class ProcessExplorerViewModel : ObservableObject, IDisposable
{
    private readonly object _refreshSync = new();
    private readonly ISystemProcessService _processService;
    private readonly IAppLogger _logger;
    private IReadOnlyList<ProcessSummary> _allProcesses =
        Array.Empty<ProcessSummary>();
    private IReadOnlyList<ProcessRowViewModel> _processes =
        Array.Empty<ProcessRowViewModel>();
    private CancellationTokenSource? _refreshCancellation;
    private CancellationTokenSource? _autoRefreshCancellation;
    private ProcessRowViewModel? _selectedProcess;
    private ProcessRowViewModel? _staleSelection;
    private string _searchText = string.Empty;
    private string _pidFilterText = string.Empty;
    private string? _filterMessage;
    private ProcessSortOption _selectedSortOption = ProcessSortOption.Name;
    private bool _sortDescending;
    private bool _isAutoRefreshEnabled;
    private bool _isBusy;
    private string _statusMessage = "Ready to scan running processes.";
    private DateTimeOffset? _lastRefreshedAt;
    private TimeSpan _autoRefreshInterval = TimeSpan.FromSeconds(2);
    private bool _disposed;

    public ProcessExplorerViewModel(
        ISystemProcessService processService,
        IAppLogger logger)
    {
        _processService = Guard.NotNull(processService);
        _logger = Guard.NotNull(logger);
        RefreshCommand = new AsyncRelayCommand(
            () => RefreshAsync(),
            () => !IsBusy);
        StartMonitoringCommand = new RelayCommand(
            RequestStartMonitoring,
            () => SelectedProcess?.CanStartMonitoring == true);
    }

    public event EventHandler<ProcessMonitoringRequestedEventArgs>?
        StartMonitoringRequested;

    public IReadOnlyList<ProcessSortOption> SortOptions { get; } =
        Enum.GetValues<ProcessSortOption>();

    public IReadOnlyList<ProcessRowViewModel> Processes
    {
        get => _processes;
        private set => SetProperty(ref _processes, value);
    }

    public ProcessRowViewModel? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (!SetProperty(ref _selectedProcess, value))
            {
                return;
            }

            StartMonitoringCommand.NotifyCanExecuteChanged();

            if (value is not null)
            {
                StatusMessage = value.IsStale
                    ? $"{value.ProcessName} (PID {value.ProcessId}) has exited."
                    : $"Selected {value.ProcessName} (PID {value.ProcessId}).";
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RebuildView();
            }
        }
    }

    public string PidFilterText
    {
        get => _pidFilterText;
        set
        {
            if (SetProperty(ref _pidFilterText, value ?? string.Empty))
            {
                RebuildView();
            }
        }
    }

    public string? FilterMessage
    {
        get => _filterMessage;
        private set => SetProperty(ref _filterMessage, value);
    }

    public ProcessSortOption SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(ref _selectedSortOption, value))
            {
                RebuildView();
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
                RebuildView();
            }
        }
    }

    public bool IsAutoRefreshEnabled
    {
        get => _isAutoRefreshEnabled;
        set
        {
            if (!SetProperty(ref _isAutoRefreshEnabled, value))
            {
                return;
            }

            if (value)
            {
                StartAutoRefresh();
            }
            else
            {
                StopAutoRefresh();
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
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public DateTimeOffset? LastRefreshedAt
    {
        get => _lastRefreshedAt;
        private set
        {
            if (SetProperty(ref _lastRefreshedAt, value))
            {
                OnPropertyChanged(nameof(LastRefreshedDisplay));
            }
        }
    }

    public string LastRefreshedDisplay => LastRefreshedAt.HasValue
        ? $"Last updated {LastRefreshedAt.Value.ToLocalTime():HH:mm:ss}"
        : "Not refreshed yet";

    public string ProcessCountDisplay =>
        $"{Processes.Count:N0} of {_allProcesses.Count:N0} processes";

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand StartMonitoringCommand { get; }

    public async Task InitializeAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(settings);
        _autoRefreshInterval = TimeSpan.FromMilliseconds(
            settings.ProcessRefreshIntervalMilliseconds);
        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancellationTokenSource currentCancellation;

        lock (_refreshSync)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            currentCancellation = _refreshCancellation;
        }

        IsBusy = true;
        StatusMessage = "Scanning running processes…";

        try
        {
            var result = await Task.Run(
                () => _processService.GetProcessesAsync(
                    currentCancellation.Token),
                currentCancellation.Token);

            if (currentCancellation.IsCancellationRequested ||
                !IsCurrentRefresh(currentCancellation))
            {
                return;
            }

            if (result.IsFailure)
            {
                if (result.Error.Code != ErrorCode.Cancelled)
                {
                    StatusMessage = result.Error.ToDisplayMessage();
                    _ = _logger.Log(
                        AppLogLevel.Error,
                        result.Error.ToDisplayMessage(),
                        result.Error.Exception);
                }

                return;
            }

            ApplyRefreshResult(result.Value);
            LastRefreshedAt = DateTimeOffset.Now;
            StatusMessage =
                $"Loaded {result.Value.Count:N0} running processes.";
        }
        catch (OperationCanceledException)
            when (currentCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            StatusMessage = "The process list could not be refreshed.";
            _ = _logger.Log(
                AppLogLevel.Error,
                StatusMessage,
                exception);
        }
        finally
        {
            if (IsCurrentRefresh(currentCancellation))
            {
                lock (_refreshSync)
                {
                    if (ReferenceEquals(
                        _refreshCancellation,
                        currentCancellation))
                    {
                        _refreshCancellation = null;
                    }
                }

                currentCancellation.Dispose();
                IsBusy = false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAutoRefresh();

        lock (_refreshSync)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
        }

        _disposed = true;
    }

    private void ApplyRefreshResult(
        IReadOnlyList<ProcessSummary> refreshedProcesses)
    {
        var previousSelection = SelectedProcess;
        _allProcesses = refreshedProcesses;
        _staleSelection = null;

        if (previousSelection is not null)
        {
            var matchingProcess = refreshedProcesses.FirstOrDefault(
                previousSelection.HasSameIdentity);

            _selectedProcess = matchingProcess is null
                ? previousSelection.MarkExited()
                : new ProcessRowViewModel(matchingProcess);
            _staleSelection = matchingProcess is null
                ? _selectedProcess
                : null;
        }

        RebuildView();
        OnPropertyChanged(nameof(SelectedProcess));
        StartMonitoringCommand.NotifyCanExecuteChanged();
    }

    private void RebuildView()
    {
        IEnumerable<ProcessSummary> query = _allProcesses;
        FilterMessage = null;

        var search = SearchText.Trim();

        if (search.Length > 0)
        {
            query = query.Where(process =>
                process.ProcessName.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ||
                (process.ExecutablePath?.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var pidFilter = PidFilterText.Trim();

        if (pidFilter.Length > 0)
        {
            if (int.TryParse(
                pidFilter,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var processId) &&
                processId >= 0)
            {
                query = query.Where(process =>
                    process.ProcessId == processId);
            }
            else
            {
                FilterMessage = "PID must be a non-negative whole number.";
                query = Enumerable.Empty<ProcessSummary>();
            }
        }

        query = ApplySort(query);

        var rows = query
            .Select(process => new ProcessRowViewModel(process))
            .ToList();

        if (_staleSelection is not null &&
            rows.All(row =>
                row.ProcessId != _staleSelection.ProcessId ||
                row.StartTime != _staleSelection.StartTime))
        {
            rows.Insert(0, _staleSelection);
        }

        Processes = rows.AsReadOnly();
        OnPropertyChanged(nameof(ProcessCountDisplay));

        if (SelectedProcess is not null)
        {
            var selected = Processes.FirstOrDefault(row =>
                ReferenceEquals(row, _staleSelection) ||
                row.HasSameIdentity(SelectedProcess.Summary));

            if (selected is not null && !ReferenceEquals(
                selected,
                SelectedProcess))
            {
                _selectedProcess = selected;
                OnPropertyChanged(nameof(SelectedProcess));
            }
        }
    }

    private IEnumerable<ProcessSummary> ApplySort(
        IEnumerable<ProcessSummary> source)
    {
        return SelectedSortOption switch
        {
            ProcessSortOption.Pid => SortDescending
                ? source.OrderByDescending(process => process.ProcessId)
                : source.OrderBy(process => process.ProcessId),
            ProcessSortOption.CpuUsage => SortNullable(
                source,
                process => process.CpuUsagePercentage),
            ProcessSortOption.WorkingSet => SortNullable(
                source,
                process => process.WorkingSetBytes),
            ProcessSortOption.PrivateMemory => SortNullable(
                source,
                process => process.PrivateMemoryBytes),
            ProcessSortOption.Architecture => SortDescending
                ? source.OrderByDescending(process => process.Architecture)
                : source.OrderBy(process => process.Architecture),
            ProcessSortOption.Status => SortDescending
                ? source.OrderByDescending(process => process.AccessStatus)
                : source.OrderBy(process => process.AccessStatus),
            _ => SortDescending
                ? source.OrderByDescending(
                    process => process.ProcessName,
                    StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(
                    process => process.ProcessName,
                    StringComparer.OrdinalIgnoreCase),
        };
    }

    private IEnumerable<ProcessSummary> SortNullable<T>(
        IEnumerable<ProcessSummary> source,
        Func<ProcessSummary, T?> selector)
        where T : struct, IComparable<T>
    {
        return SortDescending
            ? source
                .OrderBy(process => !selector(process).HasValue)
                .ThenByDescending(selector)
            : source
                .OrderBy(process => !selector(process).HasValue)
                .ThenBy(selector);
    }

    private void RequestStartMonitoring()
    {
        if (SelectedProcess?.CanStartMonitoring != true)
        {
            return;
        }

        StatusMessage =
            $"Monitoring requested for {SelectedProcess.ProcessName} " +
            $"(PID {SelectedProcess.ProcessId}).";
        StartMonitoringRequested?.Invoke(
            this,
            new ProcessMonitoringRequestedEventArgs(SelectedProcess));
    }

    private void StartAutoRefresh()
    {
        StopAutoRefresh();
        _autoRefreshCancellation = new CancellationTokenSource();
        _ = RunAutoRefreshAsync(_autoRefreshCancellation.Token);
    }

    private void StopAutoRefresh()
    {
        _autoRefreshCancellation?.Cancel();
        _autoRefreshCancellation?.Dispose();
        _autoRefreshCancellation = null;
    }

    private async Task RunAutoRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                    _autoRefreshInterval,
                    cancellationToken);
                await RefreshAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            return;
        }
        catch (Exception exception)
        {
            StatusMessage = "Automatic process refresh stopped unexpectedly.";
            IsAutoRefreshEnabled = false;
            _ = _logger.Log(
                AppLogLevel.Error,
                StatusMessage,
                exception);
        }
    }

    private bool IsCurrentRefresh(
        CancellationTokenSource cancellation)
    {
        lock (_refreshSync)
        {
            return ReferenceEquals(_refreshCancellation, cancellation);
        }
    }
}

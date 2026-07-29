using System.Globalization;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Wpf.Mvvm;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class MemoryRegionViewerViewModel
    : ObservableObject, IDisposable
{
    private readonly object _refreshSync = new();
    private readonly IMemoryRegionService _memoryRegionService;
    private readonly IMonitoringSessionService _monitoringSessionService;
    private readonly IAppLogger _logger;
    private readonly SynchronizationContext? _synchronizationContext;
    private IReadOnlyList<MemoryRegionRowViewModel> _allRegions =
        Array.Empty<MemoryRegionRowViewModel>();
    private IReadOnlyList<MemoryRegionRowViewModel> _regions =
        Array.Empty<MemoryRegionRowViewModel>();
    private CancellationTokenSource? _refreshCancellation;
    private MonitoringSession? _currentSession;
    private Guid? _loadedSessionId;
    private MemoryRegionRowViewModel? _selectedRegion;
    private string _addressSearchText = string.Empty;
    private string? _filterMessage;
    private string? _warningMessage;
    private MemoryRegionProtectionFilter _selectedProtectionFilter =
        MemoryRegionProtectionFilter.All;
    private MemoryRegionTypeFilter _selectedTypeFilter =
        MemoryRegionTypeFilter.All;
    private MemoryRegionAccessFilter _selectedAccessFilter =
        MemoryRegionAccessFilter.All;
    private MemoryRegionSortOption _selectedSortOption =
        MemoryRegionSortOption.Address;
    private bool _sortDescending;
    private bool _isBusy;
    private string _statusMessage =
        "Start monitoring a process to inspect its memory regions.";
    private DateTimeOffset? _lastRefreshedAt;
    private bool _disposed;

    public MemoryRegionViewerViewModel(
        IMemoryRegionService memoryRegionService,
        IMonitoringSessionService monitoringSessionService,
        IAppLogger logger)
    {
        _memoryRegionService = Guard.NotNull(memoryRegionService);
        _monitoringSessionService =
            Guard.NotNull(monitoringSessionService);
        _logger = Guard.NotNull(logger);
        _synchronizationContext = SynchronizationContext.Current;
        _currentSession = monitoringSessionService.CurrentSession;
        RefreshCommand = new AsyncRelayCommand(
            () => RefreshAsync(),
            () => IsSessionConnected && !IsBusy);
        OpenHexCommand = new RelayCommand(
            RequestOpenHex,
            () => SelectedRegion is not null);
        monitoringSessionService.SessionChanged += OnSessionChanged;
    }

    public event EventHandler<HexViewerRequestedEventArgs>?
        OpenHexRequested;

    public IReadOnlyList<MemoryRegionProtectionFilter>
        ProtectionFilters { get; } =
        Enum.GetValues<MemoryRegionProtectionFilter>();

    public IReadOnlyList<MemoryRegionTypeFilter> TypeFilters { get; } =
        Enum.GetValues<MemoryRegionTypeFilter>();

    public IReadOnlyList<MemoryRegionAccessFilter> AccessFilters { get; } =
        Enum.GetValues<MemoryRegionAccessFilter>();

    public IReadOnlyList<MemoryRegionSortOption> SortOptions { get; } =
        Enum.GetValues<MemoryRegionSortOption>();

    public IReadOnlyList<MemoryRegionRowViewModel> Regions
    {
        get => _regions;
        private set => SetProperty(ref _regions, value);
    }

    public MemoryRegionRowViewModel? SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (SetProperty(ref _selectedRegion, value))
            {
                OpenHexCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string AddressSearchText
    {
        get => _addressSearchText;
        set
        {
            if (SetProperty(
                ref _addressSearchText,
                value ?? string.Empty))
            {
                RebuildView();
            }
        }
    }

    public MemoryRegionProtectionFilter SelectedProtectionFilter
    {
        get => _selectedProtectionFilter;
        set
        {
            if (SetProperty(ref _selectedProtectionFilter, value))
            {
                RebuildView();
            }
        }
    }

    public MemoryRegionTypeFilter SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (SetProperty(ref _selectedTypeFilter, value))
            {
                RebuildView();
            }
        }
    }

    public MemoryRegionAccessFilter SelectedAccessFilter
    {
        get => _selectedAccessFilter;
        set
        {
            if (SetProperty(ref _selectedAccessFilter, value))
            {
                RebuildView();
            }
        }
    }

    public MemoryRegionSortOption SelectedSortOption
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

    public string? FilterMessage
    {
        get => _filterMessage;
        private set => SetProperty(ref _filterMessage, value);
    }

    public string? WarningMessage
    {
        get => _warningMessage;
        private set => SetProperty(ref _warningMessage, value);
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

    public string RegionCountDisplay =>
        $"{Regions.Count:N0} of {_allRegions.Count:N0} regions";

    public bool IsSessionConnected =>
        _currentSession?.State == MonitoringSessionState.Connected;

    public string TargetDisplay => _currentSession is null
        ? "No monitoring target"
        : $"{_currentSession.Identity.ProcessName} " +
          $"(PID {_currentSession.Identity.ProcessId})";

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand OpenHexCommand { get; }

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsSessionConnected)
        {
            StatusMessage =
                "A connected monitoring session is required.";
            return;
        }

        CancellationTokenSource currentCancellation;
        var requestedSessionId = _currentSession!.SessionId;

        lock (_refreshSync)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            currentCancellation = _refreshCancellation;
        }

        IsBusy = true;
        WarningMessage = null;
        StatusMessage = "Enumerating memory regions…";

        try
        {
            var result = await _memoryRegionService.GetRegionsAsync(
                currentCancellation.Token);

            if (currentCancellation.IsCancellationRequested ||
                !IsCurrentRefresh(currentCancellation))
            {
                return;
            }

            if (result.IsFailure)
            {
                StatusMessage = result.Error.ToDisplayMessage();

                if (result.Error.Code != ErrorCode.Cancelled)
                {
                    _ = _logger.Log(
                        AppLogLevel.Error,
                        StatusMessage,
                        result.Error.Exception);
                }

                return;
            }

            var previousSelection = SelectedRegion;
            _allRegions = result.Value.Regions
                .Select(region => new MemoryRegionRowViewModel(region))
                .ToArray();
            _loadedSessionId = requestedSessionId;
            RebuildView();
            RestoreSelection(previousSelection);
            LastRefreshedAt = DateTimeOffset.Now;
            WarningMessage = result.Value.IsPartial
                ? string.Join(
                    " · ",
                    result.Value.Warnings.Select(
                        warning => warning.ToDisplayMessage()))
                : null;
            StatusMessage = result.Value.IsPartial
                ? $"Loaded {_allRegions.Count:N0} memory regions " +
                  "with partial results."
                : $"Loaded {_allRegions.Count:N0} memory regions.";
        }
        catch (OperationCanceledException)
            when (currentCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            StatusMessage = "Memory regions could not be refreshed.";
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

        _monitoringSessionService.SessionChanged -= OnSessionChanged;

        lock (_refreshSync)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
        }

        _disposed = true;
    }

    private void RebuildView()
    {
        IEnumerable<MemoryRegionRowViewModel> query = _allRegions;
        FilterMessage = null;

        var addressText = AddressSearchText.Trim();

        if (addressText.Length > 0)
        {
            if (TryParseAddress(addressText, out var address))
            {
                query = query.Where(region =>
                    region.BaseAddress <= address &&
                    address < region.EndAddress);
            }
            else
            {
                FilterMessage =
                    "Address must be a hexadecimal x64 value.";
                query = Enumerable.Empty<MemoryRegionRowViewModel>();
            }
        }

        query = query
            .Where(MatchesProtection)
            .Where(MatchesType)
            .Where(MatchesAccess);
        query = ApplySort(query);
        Regions = Array.AsReadOnly(query.ToArray());
        OnPropertyChanged(nameof(RegionCountDisplay));

        if (SelectedRegion is not null &&
            Regions.All(region =>
                !region.HasSameIdentity(SelectedRegion)))
        {
            SelectedRegion = null;
        }
    }

    private IEnumerable<MemoryRegionRowViewModel> ApplySort(
        IEnumerable<MemoryRegionRowViewModel> source)
    {
        return SelectedSortOption switch
        {
            MemoryRegionSortOption.Size => SortDescending
                ? source.OrderByDescending(region => region.Size)
                : source.OrderBy(region => region.Size),
            MemoryRegionSortOption.AllocationBase => SortDescending
                ? source.OrderByDescending(
                    region => region.AllocationBase)
                : source.OrderBy(region => region.AllocationBase),
            MemoryRegionSortOption.State => SortDescending
                ? source.OrderByDescending(region => region.State)
                    .ThenByDescending(region => region.BaseAddress)
                : source.OrderBy(region => region.State)
                    .ThenBy(region => region.BaseAddress),
            MemoryRegionSortOption.Type => SortDescending
                ? source.OrderByDescending(region => region.Type)
                    .ThenByDescending(region => region.BaseAddress)
                : source.OrderBy(region => region.Type)
                    .ThenBy(region => region.BaseAddress),
            MemoryRegionSortOption.Protection => SortDescending
                ? source.OrderByDescending(region => region.Protection)
                    .ThenByDescending(region => region.BaseAddress)
                : source.OrderBy(region => region.Protection)
                    .ThenBy(region => region.BaseAddress),
            _ => SortDescending
                ? source.OrderByDescending(region => region.BaseAddress)
                : source.OrderBy(region => region.BaseAddress),
        };
    }

    private bool MatchesProtection(MemoryRegionRowViewModel region)
    {
        var required = SelectedProtectionFilter switch
        {
            MemoryRegionProtectionFilter.NoAccess =>
                MemoryProtection.NoAccess,
            MemoryRegionProtectionFilter.ReadOnly =>
                MemoryProtection.ReadOnly,
            MemoryRegionProtectionFilter.ReadWrite =>
                MemoryProtection.ReadWrite,
            MemoryRegionProtectionFilter.WriteCopy =>
                MemoryProtection.WriteCopy,
            MemoryRegionProtectionFilter.Execute =>
                MemoryProtection.Execute,
            MemoryRegionProtectionFilter.ExecuteRead =>
                MemoryProtection.ExecuteRead,
            MemoryRegionProtectionFilter.ExecuteReadWrite =>
                MemoryProtection.ExecuteReadWrite,
            MemoryRegionProtectionFilter.ExecuteWriteCopy =>
                MemoryProtection.ExecuteWriteCopy,
            MemoryRegionProtectionFilter.Guard =>
                MemoryProtection.Guard,
            MemoryRegionProtectionFilter.NoCache =>
                MemoryProtection.NoCache,
            MemoryRegionProtectionFilter.WriteCombine =>
                MemoryProtection.WriteCombine,
            MemoryRegionProtectionFilter.Unknown =>
                MemoryProtection.Unknown,
            _ => MemoryProtection.None,
        };

        return required == MemoryProtection.None ||
               region.Protection.HasFlag(required);
    }

    private bool MatchesType(MemoryRegionRowViewModel region)
    {
        return SelectedTypeFilter switch
        {
            MemoryRegionTypeFilter.Private =>
                region.Type == MemoryRegionType.Private,
            MemoryRegionTypeFilter.Mapped =>
                region.Type == MemoryRegionType.Mapped,
            MemoryRegionTypeFilter.Image =>
                region.Type == MemoryRegionType.Image,
            MemoryRegionTypeFilter.None =>
                region.Type == MemoryRegionType.None,
            MemoryRegionTypeFilter.Unknown =>
                region.Type == MemoryRegionType.Unknown,
            _ => true,
        };
    }

    private bool MatchesAccess(MemoryRegionRowViewModel region)
    {
        return SelectedAccessFilter switch
        {
            MemoryRegionAccessFilter.Readable => region.IsReadable,
            MemoryRegionAccessFilter.Writable => region.IsWritable,
            _ => true,
        };
    }

    private void RestoreSelection(
        MemoryRegionRowViewModel? previousSelection)
    {
        SelectedRegion = previousSelection is null
            ? null
            : Regions.FirstOrDefault(
                region => region.HasSameIdentity(previousSelection));
    }

    private void RequestOpenHex()
    {
        if (SelectedRegion is null)
        {
            return;
        }

        OpenHexRequested?.Invoke(
            this,
            new HexViewerRequestedEventArgs(
                SelectedRegion.BaseAddress,
                SelectedRegion.Region));
        StatusMessage =
            $"{SelectedRegion.BaseAddressDisplay} was sent to Hex Viewer.";
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
        var sessionChanged = _currentSession?.SessionId != session.SessionId;
        _currentSession = session;
        OnPropertyChanged(nameof(IsSessionConnected));
        OnPropertyChanged(nameof(TargetDisplay));
        RefreshCommand.NotifyCanExecuteChanged();

        if (session.State != MonitoringSessionState.Connected ||
            (sessionChanged &&
             _loadedSessionId != session.SessionId))
        {
            CancelRefresh();
            ClearRegions();
            StatusMessage = session.State ==
                MonitoringSessionState.Connected
                ? "Refresh to load the target memory regions."
                : "Memory regions are unavailable because the " +
                  $"session is {session.State}.";
        }
    }

    private void ClearRegions()
    {
        _allRegions = Array.Empty<MemoryRegionRowViewModel>();
        _loadedSessionId = null;
        Regions = Array.Empty<MemoryRegionRowViewModel>();
        SelectedRegion = null;
        WarningMessage = null;
        LastRefreshedAt = null;
        OnPropertyChanged(nameof(RegionCountDisplay));
    }

    private void CancelRefresh()
    {
        lock (_refreshSync)
        {
            _refreshCancellation?.Cancel();
        }
    }

    private bool IsCurrentRefresh(
        CancellationTokenSource cancellation)
    {
        lock (_refreshSync)
        {
            return ReferenceEquals(
                _refreshCancellation,
                cancellation);
        }
    }

    private static bool TryParseAddress(
        string value,
        out ulong address)
    {
        address = 0;
        var normalized = value.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;

        return normalized.Length > 0 &&
               ulong.TryParse(
                   normalized,
                   NumberStyles.AllowHexSpecifier,
                   CultureInfo.InvariantCulture,
                   out address);
    }
}

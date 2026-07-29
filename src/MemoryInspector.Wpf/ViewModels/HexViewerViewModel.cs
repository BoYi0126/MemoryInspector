using System.Globalization;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Wpf.Mvvm;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class HexViewerViewModel :
    ObservableObject,
    IDisposable
{
    public const int WindowSizeBytes = 4 * 1024;
    public const int BytesPerRow = 16;

    private readonly object _loadSync = new();
    private readonly IMemoryReaderService _memoryReaderService;
    private readonly IMonitoringSessionService _monitoringSessionService;
    private readonly IAppLogger _logger;
    private readonly SynchronizationContext? _synchronizationContext;
    private IReadOnlyList<HexViewerRowViewModel> _rows = [];
    private CancellationTokenSource? _loadCancellation;
    private MonitoringSession? _currentSession;
    private HexViewerRowViewModel? _selectedRow;
    private byte[] _loadedData = [];
    private ulong _windowStart;
    private int _requestedLength;
    private ulong? _rangeStart;
    private ulong? _rangeEndExclusive;
    private ulong? _searchMatchAddress;
    private int _searchMatchLength;
    private string _addressText = string.Empty;
    private string _searchText = string.Empty;
    private string? _inputMessage;
    private string? _warningMessage;
    private string _statusMessage =
        "Open a memory region or scan result to inspect bytes.";
    private DateTimeOffset? _lastRefreshedAt;
    private bool _isBusy;
    private bool _hasWindow;
    private bool _disposed;

    public HexViewerViewModel(
        IMemoryReaderService memoryReaderService,
        IMonitoringSessionService monitoringSessionService,
        IAppLogger logger)
    {
        _memoryReaderService = Guard.NotNull(memoryReaderService);
        _monitoringSessionService =
            Guard.NotNull(monitoringSessionService);
        _logger = Guard.NotNull(logger);
        _synchronizationContext = SynchronizationContext.Current;
        _currentSession = monitoringSessionService.CurrentSession;

        RefreshCommand = new AsyncRelayCommand(
            () => RefreshAsync(),
            () => CanRead);
        JumpCommand = new AsyncRelayCommand(
            () => JumpAsync(),
            () => IsSessionConnected &&
                  !IsBusy &&
                  !string.IsNullOrWhiteSpace(AddressText));
        SearchCommand = new RelayCommand(
            SearchBytes,
            () => HasWindow &&
                  !IsBusy &&
                  !string.IsNullOrWhiteSpace(SearchText));
        PreviousPageCommand = new AsyncRelayCommand(
            () => GoToPreviousPageAsync(),
            () => CanGoToPreviousPage);
        NextPageCommand = new AsyncRelayCommand(
            () => GoToNextPageAsync(),
            () => CanGoToNextPage);

        monitoringSessionService.SessionChanged += OnSessionChanged;
    }

    public IReadOnlyList<HexViewerRowViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public HexViewerRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    public string AddressText
    {
        get => _addressText;
        set
        {
            if (SetProperty(ref _addressText, value ?? string.Empty))
            {
                JumpCommand.NotifyCanExecuteChanged();
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
                SearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? InputMessage
    {
        get => _inputMessage;
        private set => SetProperty(ref _inputMessage, value);
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

    public bool HasWindow
    {
        get => _hasWindow;
        private set
        {
            if (SetProperty(ref _hasWindow, value))
            {
                OnPropertyChanged(nameof(WindowRangeDisplay));
                OnPropertyChanged(nameof(PageDisplay));
                NotifyCommands();
            }
        }
    }

    public bool IsSessionConnected =>
        _currentSession?.State == MonitoringSessionState.Connected;

    public bool CanRead =>
        IsSessionConnected && HasWindow && !IsBusy;

    public bool CanGoToPreviousPage =>
        CanRead &&
        (_rangeStart.HasValue
            ? _windowStart > _rangeStart.Value
            : _windowStart >= WindowSizeBytes);

    public bool CanGoToNextPage =>
        CanRead &&
        (_rangeEndExclusive.HasValue
            ? GetWindowEnd() < _rangeEndExclusive.Value
            : _windowStart <= ulong.MaxValue - WindowSizeBytes);

    public string TargetDisplay => _currentSession is null
        ? "No monitoring target"
        : $"{_currentSession.Identity.ProcessName} " +
          $"(PID {_currentSession.Identity.ProcessId})";

    public string WindowRangeDisplay => !HasWindow
        ? "No memory window"
        : $"0x{_windowStart:X16} – " +
          $"0x{GetWindowEnd():X16} " +
          $"({_requestedLength:N0} bytes)";

    public string PageDisplay
    {
        get
        {
            if (!HasWindow ||
                !_rangeStart.HasValue ||
                !_rangeEndExclusive.HasValue)
            {
                return "Address window";
            }

            var regionSize =
                _rangeEndExclusive.Value - _rangeStart.Value;
            var totalPages =
                ((regionSize - 1) / WindowSizeBytes) + 1;
            var currentPage =
                ((_windowStart - _rangeStart.Value) /
                 WindowSizeBytes) + 1;
            return $"Page {currentPage:N0} of {totalPages:N0}";
        }
    }

    public string LastRefreshedDisplay => LastRefreshedAt.HasValue
        ? $"Last updated {LastRefreshedAt.Value.ToLocalTime():HH:mm:ss}"
        : "Not refreshed yet";

    public string SearchMatchDisplay =>
        _searchMatchAddress.HasValue
            ? $"Match at 0x{_searchMatchAddress.Value:X16}"
            : "No active match";

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand JumpCommand { get; }

    public RelayCommand SearchCommand { get; }

    public AsyncRelayCommand PreviousPageCommand { get; }

    public AsyncRelayCommand NextPageCommand { get; }

    public async Task OpenRegionAsync(
        MemoryRegion region,
        ulong? address = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(region);

        var target = address ?? region.BaseAddress;

        if (target < region.BaseAddress ||
            target >= region.EndAddress)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                "The target address must belong to the region.");
        }

        _rangeStart = region.BaseAddress;
        _rangeEndExclusive = region.EndAddress;
        SetWindowStart(GetPageStart(target, region.BaseAddress));
        AddressText = FormatAddress(target);
        InputMessage = null;
        await RefreshAsync(cancellationToken);
    }

    public async Task OpenAddressAsync(
        ulong address,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _rangeStart = null;
        _rangeEndExclusive = null;
        SetWindowStart(AlignDown(address, WindowSizeBytes));
        AddressText = FormatAddress(address);
        InputMessage = null;
        await RefreshAsync(cancellationToken);
    }

    public async Task JumpAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!TryParseAddress(AddressText, out var address))
        {
            InputMessage =
                "Address must be a hexadecimal x64 value.";
            return;
        }

        if (_rangeStart.HasValue &&
            _rangeEndExclusive.HasValue &&
            (address < _rangeStart.Value ||
             address >= _rangeEndExclusive.Value))
        {
            InputMessage =
                "Address is outside the selected memory region.";
            return;
        }

        InputMessage = null;
        SetWindowStart(_rangeStart.HasValue
            ? GetPageStart(address, _rangeStart.Value)
            : AlignDown(address, WindowSizeBytes));
        await RefreshAsync(cancellationToken);
    }

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

        if (!HasWindow || _requestedLength <= 0)
        {
            StatusMessage =
                "Open or jump to an address before refreshing.";
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

        IsBusy = true;
        WarningMessage = null;
        StatusMessage =
            $"Reading {_requestedLength:N0}-byte memory window…";

        try
        {
            var result = await _memoryReaderService.ReadAsync(
                _windowStart,
                _requestedLength,
                new MemoryReadOptions(WindowSizeBytes),
                currentCancellation.Token);

            if (currentCancellation.IsCancellationRequested ||
                !IsCurrentLoad(currentCancellation))
            {
                return;
            }

            if (result.IsFailure)
            {
                _loadedData = [];
                BuildRows();
                StatusMessage = result.Error.ToDisplayMessage();
                WarningMessage =
                    "The entire window is unreadable; bytes are shown as ??.";
                _ = _logger.Log(
                    AppLogLevel.Warning,
                    StatusMessage,
                    result.Error.Exception);
                return;
            }

            _loadedData = result.Value.Data.ToArray();
            _searchMatchAddress = null;
            _searchMatchLength = 0;
            BuildRows();
            LastRefreshedAt = DateTimeOffset.Now;
            WarningMessage = result.Value.IsComplete
                ? null
                : string.Join(
                    " • ",
                    result.Value.Warnings.Select(
                        warning => warning.ToDisplayMessage())
                        .Append(
                            "Unread bytes are shown as ?? and ·."));
            StatusMessage = result.Value.IsComplete
                ? $"Loaded {_loadedData.Length:N0} bytes."
                : $"Loaded {_loadedData.Length:N0} of " +
                  $"{_requestedLength:N0} bytes.";
        }
        catch (OperationCanceledException)
            when (currentCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _loadedData = [];
            BuildRows();
            StatusMessage = "The memory window could not be refreshed.";
            WarningMessage =
                "The entire window is unreadable; bytes are shown as ??.";
            _ = _logger.Log(
                AppLogLevel.Error,
                StatusMessage,
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

    public async Task GoToPreviousPageAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanGoToPreviousPage)
        {
            return;
        }

        var minimum = _rangeStart ?? 0;
        SetWindowStart(
            _windowStart - Math.Min(
                _windowStart - minimum,
                (ulong)WindowSizeBytes));
        AddressText = FormatAddress(_windowStart);
        await RefreshAsync(cancellationToken);
    }

    public async Task GoToNextPageAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanGoToNextPage)
        {
            return;
        }

        SetWindowStart(
            checked(_windowStart + (ulong)WindowSizeBytes));
        AddressText = FormatAddress(_windowStart);
        await RefreshAsync(cancellationToken);
    }

    public void SearchBytes()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!TryParseBytePattern(SearchText, out var pattern))
        {
            InputMessage =
                "Search bytes must use pairs of hexadecimal digits.";
            ClearSearchMatch();
            return;
        }

        InputMessage = null;
        var index = _loadedData.AsSpan().IndexOf(pattern);

        if (index < 0)
        {
            ClearSearchMatch();
            StatusMessage =
                "Byte pattern was not found in the current window.";
            return;
        }

        _searchMatchAddress = _windowStart + (ulong)index;
        _searchMatchLength = pattern.Length;
        BuildRows();
        SelectedRow = Rows.FirstOrDefault(row =>
            row.IsSearchMatch);
        OnPropertyChanged(nameof(SearchMatchDisplay));
        StatusMessage =
            $"Found {pattern.Length:N0} bytes at " +
            $"{FormatAddress(_searchMatchAddress.Value)}.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _monitoringSessionService.SessionChanged -= OnSessionChanged;

        lock (_loadSync)
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
        }

        _disposed = true;
    }

    internal static bool TryParseBytePattern(
        string value,
        out byte[] pattern)
    {
        pattern = [];

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal);

        if (normalized.Length == 0 ||
            normalized.Length % 2 != 0)
        {
            return false;
        }

        var bytes = new byte[normalized.Length / 2];

        for (var index = 0; index < bytes.Length; index++)
        {
            if (!byte.TryParse(
                normalized.AsSpan(index * 2, 2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out bytes[index]))
            {
                return false;
            }
        }

        pattern = bytes;
        return true;
    }

    private void SetWindowStart(ulong value)
    {
        _windowStart = value;
        _requestedLength = CalculateRequestedLength();
        HasWindow = _requestedLength > 0;
        _loadedData = [];
        ClearSearchMatch();
        Rows = [];
        LastRefreshedAt = null;
        WarningMessage = null;
        OnPropertyChanged(nameof(WindowRangeDisplay));
        OnPropertyChanged(nameof(PageDisplay));
        NotifyCommands();
    }

    private int CalculateRequestedLength()
    {
        if (_rangeEndExclusive.HasValue)
        {
            if (_windowStart >= _rangeEndExclusive.Value)
            {
                return 0;
            }

            return (int)Math.Min(
                (ulong)WindowSizeBytes,
                _rangeEndExclusive.Value - _windowStart);
        }

        return (int)Math.Min(
            (ulong)WindowSizeBytes,
            ulong.MaxValue - _windowStart);
    }

    private ulong GetWindowEnd()
    {
        return _windowStart + (ulong)_requestedLength;
    }

    private void BuildRows()
    {
        var rows = new List<HexViewerRowViewModel>(
            (_requestedLength + BytesPerRow - 1) /
            BytesPerRow);

        for (var offset = 0;
             offset < _requestedLength;
             offset += BytesPerRow)
        {
            var rowLength = Math.Min(
                BytesPerRow,
                _requestedLength - offset);
            var availableLength = Math.Min(
                rowLength,
                Math.Max(0, _loadedData.Length - offset));
            var rowData = offset < _loadedData.Length
                ? _loadedData.AsSpan(offset, availableLength)
                : ReadOnlySpan<byte>.Empty;
            rows.Add(
                new HexViewerRowViewModel(
                    _windowStart + (ulong)offset,
                    GetDisplayOffset((ulong)offset),
                    rowData,
                    rowLength,
                    _searchMatchAddress,
                    _searchMatchLength));
        }

        Rows = Array.AsReadOnly(rows.ToArray());
        SelectedRow = _searchMatchAddress.HasValue
            ? Rows.FirstOrDefault(row => row.IsSearchMatch)
            : null;
    }

    private ulong GetDisplayOffset(ulong rowOffset)
    {
        return _rangeStart.HasValue
            ? (_windowStart - _rangeStart.Value) + rowOffset
            : rowOffset;
    }

    private void ClearSearchMatch()
    {
        _searchMatchAddress = null;
        _searchMatchLength = 0;
        OnPropertyChanged(nameof(SearchMatchDisplay));

        if (Rows.Count > 0)
        {
            BuildRows();
        }
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
        var changed = _currentSession?.SessionId != session.SessionId;
        _currentSession = session;
        OnPropertyChanged(nameof(IsSessionConnected));
        OnPropertyChanged(nameof(TargetDisplay));
        NotifyCommands();

        if (changed ||
            session.State != MonitoringSessionState.Connected)
        {
            CancelLoad();
            ClearWindow();
            StatusMessage = session.State ==
                MonitoringSessionState.Connected
                ? "Open a memory region or scan result to inspect bytes."
                : "Hex Viewer is unavailable because the " +
                  $"session is {session.State}.";
        }
    }

    private void ClearWindow()
    {
        _rangeStart = null;
        _rangeEndExclusive = null;
        _windowStart = 0;
        _requestedLength = 0;
        _loadedData = [];
        Rows = [];
        SelectedRow = null;
        HasWindow = false;
        AddressText = string.Empty;
        InputMessage = null;
        WarningMessage = null;
        LastRefreshedAt = null;
        ClearSearchMatch();
    }

    private void CancelLoad()
    {
        lock (_loadSync)
        {
            _loadCancellation?.Cancel();
        }
    }

    private bool IsCurrentLoad(
        CancellationTokenSource cancellation)
    {
        lock (_loadSync)
        {
            return ReferenceEquals(_loadCancellation, cancellation);
        }
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        JumpCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
    }

    private ulong GetPageStart(ulong address, ulong rangeStart)
    {
        return rangeStart +
               ((address - rangeStart) /
                WindowSizeBytes) *
               WindowSizeBytes;
    }

    private static ulong AlignDown(ulong address, int alignment)
    {
        return address - address % (ulong)alignment;
    }

    private static bool TryParseAddress(
        string value,
        out ulong address)
    {
        address = 0;
        var normalized = value.Trim();

        if (normalized.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return normalized.Length > 0 &&
               ulong.TryParse(
                   normalized,
                   NumberStyles.AllowHexSpecifier,
                   CultureInfo.InvariantCulture,
                   out address);
    }

    private static string FormatAddress(ulong address)
    {
        return $"0x{address:X16}";
    }
}

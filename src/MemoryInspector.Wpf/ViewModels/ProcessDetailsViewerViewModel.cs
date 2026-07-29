using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.ProcessInspection;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Wpf.Mvvm;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class ProcessDetailsViewerViewModel :
    ObservableObject,
    IDisposable
{
    private readonly object _refreshSync = new();
    private readonly IProcessModuleService _moduleService;
    private readonly IProcessThreadService _threadService;
    private readonly IMonitoringSessionService _sessionService;
    private readonly IAppLogger _logger;
    private readonly SynchronizationContext? _synchronizationContext;
    private IReadOnlyList<ProcessModuleRowViewModel> _allModules =
        [];
    private IReadOnlyList<ProcessModuleRowViewModel> _modules = [];
    private IReadOnlyList<ProcessThreadRowViewModel> _allThreads =
        [];
    private IReadOnlyList<ProcessThreadRowViewModel> _threads = [];
    private ProcessModuleRowViewModel? _selectedModule;
    private ProcessThreadRowViewModel? _selectedThread;
    private MonitoringSession? _currentSession;
    private CancellationTokenSource? _refreshCancellation;
    private string _moduleSearchText = string.Empty;
    private string _threadSearchText = string.Empty;
    private ProcessModuleSortOption _moduleSort =
        ProcessModuleSortOption.Name;
    private ProcessThreadSortOption _threadSort =
        ProcessThreadSortOption.ThreadId;
    private bool _moduleSortDescending;
    private bool _threadSortDescending;
    private bool _isBusy;
    private string _statusMessage =
        "Start monitoring a process to inspect modules and threads.";
    private string? _warningMessage;
    private string? _errorMessage;
    private DateTimeOffset? _lastRefreshedAt;
    private bool _disposed;

    public ProcessDetailsViewerViewModel(
        IProcessModuleService moduleService,
        IProcessThreadService threadService,
        IMonitoringSessionService sessionService,
        IAppLogger logger)
    {
        _moduleService = Guard.NotNull(moduleService);
        _threadService = Guard.NotNull(threadService);
        _sessionService = Guard.NotNull(sessionService);
        _logger = Guard.NotNull(logger);
        _currentSession = sessionService.CurrentSession;
        _synchronizationContext = SynchronizationContext.Current;
        RefreshCommand = new AsyncRelayCommand(
            () => RefreshAsync(),
            () => IsSessionConnected && !IsBusy);
        sessionService.SessionChanged += OnSessionChanged;
    }

    public IReadOnlyList<ProcessModuleSortOption>
        ModuleSortOptions { get; } =
        Enum.GetValues<ProcessModuleSortOption>();

    public IReadOnlyList<ProcessThreadSortOption>
        ThreadSortOptions { get; } =
        Enum.GetValues<ProcessThreadSortOption>();

    public IReadOnlyList<ProcessModuleRowViewModel> Modules
    {
        get => _modules;
        private set => SetProperty(ref _modules, value);
    }

    public IReadOnlyList<ProcessThreadRowViewModel> Threads
    {
        get => _threads;
        private set => SetProperty(ref _threads, value);
    }

    public ProcessModuleRowViewModel? SelectedModule
    {
        get => _selectedModule;
        set => SetProperty(ref _selectedModule, value);
    }

    public ProcessThreadRowViewModel? SelectedThread
    {
        get => _selectedThread;
        set => SetProperty(ref _selectedThread, value);
    }

    public string ModuleSearchText
    {
        get => _moduleSearchText;
        set
        {
            if (SetProperty(
                ref _moduleSearchText,
                value ?? string.Empty))
            {
                RebuildModules();
            }
        }
    }

    public string ThreadSearchText
    {
        get => _threadSearchText;
        set
        {
            if (SetProperty(
                ref _threadSearchText,
                value ?? string.Empty))
            {
                RebuildThreads();
            }
        }
    }

    public ProcessModuleSortOption SelectedModuleSort
    {
        get => _moduleSort;
        set
        {
            if (SetProperty(ref _moduleSort, value))
            {
                RebuildModules();
            }
        }
    }

    public ProcessThreadSortOption SelectedThreadSort
    {
        get => _threadSort;
        set
        {
            if (SetProperty(ref _threadSort, value))
            {
                RebuildThreads();
            }
        }
    }

    public bool ModuleSortDescending
    {
        get => _moduleSortDescending;
        set
        {
            if (SetProperty(
                ref _moduleSortDescending,
                value))
            {
                RebuildModules();
            }
        }
    }

    public bool ThreadSortDescending
    {
        get => _threadSortDescending;
        set
        {
            if (SetProperty(
                ref _threadSortDescending,
                value))
            {
                RebuildThreads();
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

    public bool IsSessionConnected =>
        _currentSession?.State == MonitoringSessionState.Connected;

    public string TargetDisplay => _currentSession is null
        ? "No monitoring target"
        : $"{_currentSession.Identity.ProcessName} " +
          $"(PID {_currentSession.Identity.ProcessId})";

    public string ModuleCountDisplay =>
        $"{Modules.Count:N0} of {_allModules.Count:N0} modules";

    public string ThreadCountDisplay =>
        $"{Threads.Count:N0} of {_allThreads.Count:N0} threads";

    public string LastRefreshedDisplay =>
        LastRefreshedAt.HasValue
            ? $"Last updated " +
              $"{LastRefreshedAt.Value.ToLocalTime():HH:mm:ss}"
            : "Not refreshed yet";

    public AsyncRelayCommand RefreshCommand { get; }

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
        ErrorMessage = null;
        WarningMessage = null;
        StatusMessage = "Enumerating modules and threads…";

        try
        {
            var moduleTask = _moduleService.GetModulesAsync(
                currentCancellation.Token);
            var threadTask = _threadService.GetThreadsAsync(
                currentCancellation.Token);
            await Task.WhenAll(moduleTask, threadTask);

            if (currentCancellation.IsCancellationRequested ||
                !IsCurrentRefresh(currentCancellation) ||
                _currentSession?.SessionId != requestedSessionId)
            {
                return;
            }

            var moduleResult = await moduleTask;
            var threadResult = await threadTask;
            ApplyModuleResult(moduleResult);
            ApplyThreadResult(threadResult);
            LastRefreshedAt = DateTimeOffset.Now;
            StatusMessage =
                $"Loaded {_allModules.Count:N0} module(s) and " +
                $"{_allThreads.Count:N0} thread(s).";
            ErrorMessage = JoinMessages(
                moduleResult.IsFailure
                    ? moduleResult.Error.ToDisplayMessage()
                    : null,
                threadResult.IsFailure
                    ? threadResult.Error.ToDisplayMessage()
                    : null);
            WarningMessage = JoinMessages(
                moduleResult.IsSuccess &&
                moduleResult.Value.IsPartial
                    ? "Some module fields could not be read."
                    : null,
                threadResult.IsSuccess &&
                threadResult.Value.IsPartial
                    ? "Some thread fields could not be read."
                    : null);
        }
        catch (OperationCanceledException)
            when (currentCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage =
                "Modules and threads could not be refreshed.";
            StatusMessage = ErrorMessage;
            _ = _logger.Log(
                AppLogLevel.Error,
                ErrorMessage,
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

        _sessionService.SessionChanged -= OnSessionChanged;

        lock (_refreshSync)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
        }

        _disposed = true;
    }

    private void ApplyModuleResult(
        Result<ProcessModuleQueryResult> result)
    {
        var previous = SelectedModule;
        _allModules = result.IsSuccess
            ? Array.AsReadOnly(
                result.Value.Modules
                    .Select(module =>
                        new ProcessModuleRowViewModel(module))
                    .ToArray())
            : [];
        RebuildModules();
        SelectedModule = previous is null
            ? null
            : Modules.FirstOrDefault(module =>
                module.HasSameIdentity(previous));
    }

    private void ApplyThreadResult(
        Result<ProcessThreadQueryResult> result)
    {
        var previousId = SelectedThread?.ThreadId;
        _allThreads = result.IsSuccess
            ? Array.AsReadOnly(
                result.Value.Threads
                    .Select(thread =>
                        new ProcessThreadRowViewModel(thread))
                    .ToArray())
            : [];
        RebuildThreads();
        SelectedThread = previousId.HasValue
            ? Threads.FirstOrDefault(thread =>
                thread.ThreadId == previousId.Value)
            : null;
    }

    private void RebuildModules()
    {
        IEnumerable<ProcessModuleRowViewModel> query =
            _allModules;
        var search = ModuleSearchText.Trim();

        if (search.Length > 0)
        {
            query = query.Where(module =>
                Contains(module.Name, search) ||
                Contains(module.Module.Path, search) ||
                Contains(module.Module.Version, search) ||
                Contains(module.BaseAddressDisplay, search));
        }

        query = SelectedModuleSort switch
        {
            ProcessModuleSortOption.BaseAddress =>
                Order(query, module => module.BaseAddress),
            ProcessModuleSortOption.Size =>
                Order(query, module => module.Size),
            ProcessModuleSortOption.Path =>
                Order(query, module => module.Module.Path),
            ProcessModuleSortOption.Version =>
                Order(query, module => module.Module.Version),
            _ => Order(query, module => module.Name),
        };
        Modules = Array.AsReadOnly(query.ToArray());
        OnPropertyChanged(nameof(ModuleCountDisplay));
    }

    private void RebuildThreads()
    {
        IEnumerable<ProcessThreadRowViewModel> query =
            _allThreads;
        var search = ThreadSearchText.Trim();

        if (search.Length > 0)
        {
            query = query.Where(thread =>
                thread.ThreadId.ToString().Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ||
                Contains(thread.Thread.State, search));
        }

        query = SelectedThreadSort switch
        {
            ProcessThreadSortOption.State =>
                Order(query, thread => thread.Thread.State),
            ProcessThreadSortOption.Priority =>
                Order(query, thread => thread.Priority),
            ProcessThreadSortOption.StartTime =>
                Order(query, thread => thread.StartTime),
            ProcessThreadSortOption.CpuTime =>
                Order(query, thread => thread.CpuTime),
            _ => Order(query, thread => thread.ThreadId),
        };
        Threads = Array.AsReadOnly(query.ToArray());
        OnPropertyChanged(nameof(ThreadCountDisplay));
    }

    private IEnumerable<T> Order<T, TKey>(
        IEnumerable<T> source,
        Func<T, TKey> keySelector)
    {
        return (typeof(T) == typeof(ProcessModuleRowViewModel)
                ? ModuleSortDescending
                : ThreadSortDescending)
            ? source.OrderByDescending(keySelector)
            : source.OrderBy(keySelector);
    }

    private void OnSessionChanged(
        object? sender,
        MonitoringSessionChangedEventArgs eventArgs)
    {
        if (_synchronizationContext is not null &&
            SynchronizationContext.Current !=
            _synchronizationContext)
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
        var changed = _currentSession?.SessionId !=
            session.SessionId;
        _currentSession = session;
        OnPropertyChanged(nameof(IsSessionConnected));
        OnPropertyChanged(nameof(TargetDisplay));
        RefreshCommand.NotifyCanExecuteChanged();

        if (session.State != MonitoringSessionState.Connected ||
            changed)
        {
            lock (_refreshSync)
            {
                _refreshCancellation?.Cancel();
            }

            Clear();
            StatusMessage =
                session.State == MonitoringSessionState.Connected
                    ? "Refresh to enumerate modules and threads."
                    : "Modules and threads are unavailable because " +
                      $"the session is {session.State}.";
        }
    }

    private void Clear()
    {
        _allModules = [];
        _allThreads = [];
        Modules = [];
        Threads = [];
        SelectedModule = null;
        SelectedThread = null;
        WarningMessage = null;
        ErrorMessage = null;
        LastRefreshedAt = null;
        OnPropertyChanged(nameof(ModuleCountDisplay));
        OnPropertyChanged(nameof(ThreadCountDisplay));
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

    private static bool Contains(
        string? value,
        string search)
    {
        return value?.Contains(
            search,
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? JoinMessages(
        string? first,
        string? second)
    {
        var messages = new[] { first, second }
            .Where(message =>
                !string.IsNullOrWhiteSpace(message))
            .ToArray();
        return messages.Length == 0
            ? null
            : string.Join(" | ", messages);
    }
}

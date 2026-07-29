using System.Collections.ObjectModel;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Temporary;
using MemoryInspector.Common;
using MemoryInspector.Wpf.Mvvm;
using MemoryInspector.Wpf.Services;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class TemporaryManagerViewModel : ObservableObject
{
    private readonly ITemporaryManagerService _temporaryManager;
    private readonly IFilterPipelineService _pipeline;
    private readonly IUserConfirmationService _confirmationService;
    private TemporarySessionRowViewModel? _selectedSession;
    private TemporaryBranchOption? _selectedBranch;
    private bool _includePinned;
    private bool _isBusy;
    private string _statusMessage =
        "Temporary storage has not been inspected.";
    private string? _errorMessage;
    private string _totalSize = "0 B";
    private string _cacheSize = "0 B";
    private int _sessionCount;
    private int _snapshotCount;
    private int _incompleteFileCount;

    public TemporaryManagerViewModel(
        ITemporaryManagerService temporaryManager,
        IFilterPipelineService pipeline,
        IUserConfirmationService confirmationService)
    {
        _temporaryManager = temporaryManager ??
            throw new ArgumentNullException(
                nameof(temporaryManager));
        _pipeline = pipeline ??
            throw new ArgumentNullException(nameof(pipeline));
        _confirmationService = confirmationService ??
            throw new ArgumentNullException(
                nameof(confirmationService));
        Sessions = [];
        Branches = [];
        RefreshCommand = new AsyncRelayCommand(
            RefreshAsync,
            () => !IsBusy);
        AutomaticCleanupCommand = new AsyncRelayCommand(
            RunAutomaticCleanupAsync,
            () => !IsBusy);
        DeleteCurrentNodeCommand = new AsyncRelayCommand(
            DeleteCurrentNodeAsync,
            () => !IsBusy &&
                  _pipeline.CurrentState is not null);
        DeleteBranchCommand = new AsyncRelayCommand(
            DeleteSelectedBranchAsync,
            () => !IsBusy && SelectedBranch is not null);
        DeleteSessionCommand = new AsyncRelayCommand(
            DeleteSelectedSessionAsync,
            () => !IsBusy && SelectedSession is not null);
        DeleteAllCommand = new AsyncRelayCommand(
            DeleteAllAsync,
            () => !IsBusy && Sessions.Count > 0);
        CompactSessionCommand = new AsyncRelayCommand(
            CompactSelectedSessionAsync,
            () => !IsBusy && SelectedSession is not null);
        OpenTempFolderCommand = new RelayCommand(OpenTempFolder);
    }

    public ObservableCollection<TemporarySessionRowViewModel>
        Sessions { get; }

    public ObservableCollection<TemporaryBranchOption>
        Branches { get; }

    public TemporarySessionRowViewModel? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                NotifyCommands();
            }
        }
    }

    public TemporaryBranchOption? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (SetProperty(ref _selectedBranch, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool IncludePinned
    {
        get => _includePinned;
        set => SetProperty(ref _includePinned, value);
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

    public string TempDirectory => _temporaryManager.TempDirectory;

    public string TotalSize
    {
        get => _totalSize;
        private set => SetProperty(ref _totalSize, value);
    }

    public string CacheSize
    {
        get => _cacheSize;
        private set => SetProperty(ref _cacheSize, value);
    }

    public int SessionCount
    {
        get => _sessionCount;
        private set => SetProperty(ref _sessionCount, value);
    }

    public int SnapshotCount
    {
        get => _snapshotCount;
        private set => SetProperty(ref _snapshotCount, value);
    }

    public int IncompleteFileCount
    {
        get => _incompleteFileCount;
        private set => SetProperty(
            ref _incompleteFileCount,
            value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand AutomaticCleanupCommand { get; }

    public AsyncRelayCommand DeleteCurrentNodeCommand { get; }

    public AsyncRelayCommand DeleteBranchCommand { get; }

    public AsyncRelayCommand DeleteSessionCommand { get; }

    public AsyncRelayCommand DeleteAllCommand { get; }

    public AsyncRelayCommand CompactSessionCommand { get; }

    public RelayCommand OpenTempFolderCommand { get; }

    public Task InitializeAsync()
    {
        return RefreshAsync();
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var display = (double)value;
        var unit = 0;

        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:N0} {units[unit]}"
            : $"{display:N2} {units[unit]}";
    }

    private async Task RefreshAsync()
    {
        await RunAsync(
            () => _temporaryManager.InspectAsync(),
            snapshot =>
            {
                var selectedId = SelectedSession?.SessionId;
                Sessions.Clear();

                foreach (var session in snapshot.Sessions)
                {
                    Sessions.Add(
                        new TemporarySessionRowViewModel(session));
                }

                SelectedSession = Sessions.FirstOrDefault(row =>
                        row.SessionId == selectedId) ??
                    Sessions.FirstOrDefault(row =>
                        row.Session.IsCurrent) ??
                    Sessions.FirstOrDefault();
                TotalSize = FormatBytes(
                    snapshot.Statistics.TotalBytes);
                CacheSize = FormatBytes(
                    snapshot.Statistics.CachedBytes);
                SessionCount = snapshot.Statistics.SessionCount;
                SnapshotCount =
                    snapshot.Statistics.SnapshotCount;
                IncompleteFileCount =
                    snapshot.Statistics.IncompleteFileCount;
                RefreshBranches();
                StatusMessage =
                    $"Inspected {SessionCount:N0} session(s) and " +
                    $"{SnapshotCount:N0} snapshot(s).";
            },
            "Inspecting temporary storage…");
    }

    private async Task RunAutomaticCleanupAsync()
    {
        await RunReportOperationAsync(
            () => _temporaryManager.RunAutomaticCleanupAsync(),
            "Run automatic cleanup now?",
            "Automatic cleanup",
            "Running retention and incomplete-file cleanup…");
    }

    private async Task DeleteCurrentNodeAsync()
    {
        await RunReportOperationAsync(
            () => _temporaryManager.DeleteCurrentNodeAsync(),
            "Delete the current leaf scan node and its temporary " +
            "snapshot? The parent node will become active.",
            "Delete current temporary node",
            "Deleting current temporary node…");
    }

    private async Task DeleteSelectedBranchAsync()
    {
        var branch = SelectedBranch;

        if (branch is null)
        {
            return;
        }

        await RunReportOperationAsync(
            () => _temporaryManager.DeleteBranchAsync(
                branch.RoundId),
            $"Delete branch '{branch.DisplayName}' and every " +
            "temporary snapshot below it?",
            "Delete temporary branch",
            "Deleting temporary branch…");
    }

    private async Task DeleteSelectedSessionAsync()
    {
        var session = SelectedSession;

        if (session is null)
        {
            return;
        }

        var pinnedText = IncludePinned
            ? " Pinned nodes will also be permanently removed."
            : " Sessions containing pinned nodes will be retained.";
        await RunReportOperationAsync(
            () => _temporaryManager.DeleteSessionAsync(
                session.SessionId,
                IncludePinned),
            $"Delete temporary session {session.SessionId:D}?" +
            pinnedText,
            "Delete temporary session",
            "Deleting temporary session…");
    }

    private async Task DeleteAllAsync()
    {
        var pinnedText = IncludePinned
            ? " This includes every pinned session."
            : " Pinned or unreadable sessions will be retained.";
        await RunReportOperationAsync(
            () => _temporaryManager.DeleteAllAsync(IncludePinned),
            "Delete all temporary scan sessions?" + pinnedText,
            "Delete all temporary data",
            "Deleting temporary sessions…");
    }

    private async Task CompactSelectedSessionAsync()
    {
        var session = SelectedSession;

        if (session is null)
        {
            return;
        }

        await RunReportOperationAsync(
            () => _temporaryManager.CompactSessionAsync(
                session.SessionId),
            $"Compact session {session.SessionId:D}? Orphaned " +
            "snapshots and incomplete files will be removed.",
            "Compact temporary session",
            "Compacting and verifying scan tree…");
    }

    private void OpenTempFolder()
    {
        var result = _temporaryManager.OpenTempFolder();
        ErrorMessage = result.IsFailure
            ? result.Error.ToDisplayMessage()
            : null;
        StatusMessage = result.IsSuccess
            ? "Opened the temporary storage folder."
            : ErrorMessage!;
    }

    private async Task RunReportOperationAsync(
        Func<Task<Result<TemporaryOperationReport>>> operation,
        string confirmationMessage,
        string confirmationTitle,
        string progressMessage)
    {
        if (!_confirmationService.Confirm(
            confirmationTitle,
            confirmationMessage))
        {
            return;
        }

        TemporaryOperationReport? completedReport = null;
        await RunAsync(
            operation,
            report =>
            {
                completedReport = report;
            },
            progressMessage);

        if (ErrorMessage is null && completedReport is not null)
        {
            await RefreshAsync();

            if (ErrorMessage is null)
            {
                StatusMessage = FormatReport(completedReport);
            }
        }
    }

    private async Task RunAsync<T>(
        Func<Task<Result<T>>> operation,
        Action<T> onSuccess,
        string progressMessage)
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = progressMessage;

        try
        {
            var result = await operation();

            if (result.IsFailure)
            {
                ErrorMessage = result.Error.ToDisplayMessage();
                StatusMessage = ErrorMessage;
                return;
            }

            onSuccess(result.Value);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            StatusMessage =
                "The temporary storage operation failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshBranches()
    {
        var selectedId = SelectedBranch?.RoundId;
        Branches.Clear();
        var state = _pipeline.CurrentState;

        if (state is null)
        {
            SelectedBranch = null;
            return;
        }

        foreach (var round in state.Rounds
                     .Where(round =>
                         round.ParentRoundId is not null)
                     .OrderBy(round => round.RoundNumber))
        {
            Branches.Add(
                new TemporaryBranchOption(
                    round.RoundId,
                    $"#{round.RoundNumber:N0} {round.Name}" +
                    (round.IsPinned ? " (pinned)" : string.Empty)));
        }

        SelectedBranch = Branches.FirstOrDefault(branch =>
                branch.RoundId == selectedId) ??
            Branches.FirstOrDefault(branch =>
                branch.RoundId ==
                state.ActiveRound.RoundId) ??
            Branches.FirstOrDefault();
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        AutomaticCleanupCommand.NotifyCanExecuteChanged();
        DeleteCurrentNodeCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
        DeleteSessionCommand.NotifyCanExecuteChanged();
        DeleteAllCommand.NotifyCanExecuteChanged();
        CompactSessionCommand.NotifyCanExecuteChanged();
    }

    private static string FormatReport(
        TemporaryOperationReport report)
    {
        return
            $"Completed: {report.DeletedSessionCount:N0} session(s), " +
            $"{report.DeletedSnapshotCount:N0} snapshot(s), " +
            $"{report.DeletedFileCount:N0} file(s), " +
            $"{FormatBytes(report.ReclaimedBytes)} reclaimed; " +
            $"{report.RecoveredFileCount:N0} recovered, " +
            $"{report.DiscardedIncompleteFileCount:N0} incomplete " +
            $"discarded, {report.RetainedPinnedSessionCount:N0} " +
            $"pinned/unreadable session(s) retained.";
    }

}

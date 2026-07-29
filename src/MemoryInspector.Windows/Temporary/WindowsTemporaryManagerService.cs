using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.History;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Application.Temporary;
using MemoryInspector.Common;

namespace MemoryInspector.Windows.Temporary;

public sealed partial class WindowsTemporaryManagerService(
    IAppPathService pathService,
    ISettingsService settingsService,
    IFilterPipelineService pipeline,
    IScanHistoryStore historyStore,
    ISnapshotStorage snapshotStorage,
    ISnapshotCacheManager cacheManager,
    TimeProvider timeProvider) :
    ITemporaryManagerService,
    IDisposable
{
    private readonly IAppPathService _pathService =
        Guard.NotNull(pathService);
    private readonly ISettingsService _settingsService =
        Guard.NotNull(settingsService);
    private readonly IFilterPipelineService _pipeline =
        Guard.NotNull(pipeline);
    private readonly IScanHistoryStore _historyStore =
        Guard.NotNull(historyStore);
    private readonly ISnapshotStorage _snapshotStorage =
        Guard.NotNull(snapshotStorage);
    private readonly ISnapshotCacheManager _cacheManager =
        Guard.NotNull(cacheManager);
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public string TempDirectory => _pathService.TempDirectory;

    public async Task<Result<TemporaryStorageSnapshot>> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var enter = await EnterAsync(
                "Temporary storage inspection was cancelled.",
                cancellationToken)
            .ConfigureAwait(false);

        if (enter.IsFailure)
        {
            return Result<TemporaryStorageSnapshot>.Failure(
                enter.Error);
        }

        try
        {
            var ensure = _pathService.EnsureDirectories();

            if (ensure.IsFailure)
            {
                return Result<TemporaryStorageSnapshot>.Failure(
                    ensure.Error);
            }

            var currentSessionId = GetCurrentSessionId();
            var sessions = new List<TemporarySessionInfo>();

            foreach (var (sessionId, directory) in
                     EnumerateSessionDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sessions.Add(await InspectSessionAsync(
                        sessionId,
                        directory,
                        currentSessionId,
                        cancellationToken)
                    .ConfigureAwait(false));
            }

            var cacheUsage = await _cacheManager
                .GetUsageAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (cacheUsage.IsFailure)
            {
                return Result<TemporaryStorageSnapshot>.Failure(
                    cacheUsage.Error);
            }

            var ordered = sessions
                .OrderByDescending(session => session.IsCurrent)
                .ThenByDescending(session =>
                    session.LastModifiedAt)
                .ToArray();
            var statistics = new TemporaryStorageStatistics(
                ordered.Length,
                ordered.Sum(session => session.FileCount),
                ordered.Sum(session => session.SnapshotCount),
                ordered.Sum(session =>
                    session.IncompleteFileCount),
                ordered.Sum(session => session.PinnedNodeCount),
                ordered.Sum(session => session.TotalBytes),
                cacheUsage.Value.MemoryBytes);
            return Result<TemporaryStorageSnapshot>.Success(
                new TemporaryStorageSnapshot(
                    Array.AsReadOnly(ordered),
                    statistics));
        }
        catch (Exception exception)
        {
            return Failure<TemporaryStorageSnapshot>(
                exception,
                "Temporary storage could not be inspected.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<TemporaryOperationReport>>
        RunAutomaticCleanupAsync(
            CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var enter = await EnterAsync(
                "Automatic temporary cleanup was cancelled.",
                cancellationToken)
            .ConfigureAwait(false);

        if (enter.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                enter.Error);
        }

        try
        {
            var idle = EnsureScanIdle();

            if (idle.IsFailure)
            {
                return Result<TemporaryOperationReport>.Failure(
                    idle.Error);
            }

            var settings = await _settingsService
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);

            if (settings.IsFailure)
            {
                return Result<TemporaryOperationReport>.Failure(
                    settings.Error);
            }

            var report = new TemporaryOperationReport();
            var cutoff = _timeProvider.GetUtcNow().Subtract(
                TimeSpan.FromDays(
                    settings.Value.TempRetentionDays));
            var currentSessionId = GetCurrentSessionId();

            foreach (var (sessionId, directory) in
                     EnumerateSessionDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lastModifiedAt = new DateTimeOffset(
                    Directory.GetLastWriteTimeUtc(directory));
                var recovery = await RecoverIncompleteCoreAsync(
                        sessionId,
                        directory,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (recovery.IsFailure)
                {
                    return Result<TemporaryOperationReport>.Failure(
                        recovery.Error);
                }

                report = report.Add(recovery.Value);

                if (sessionId == currentSessionId ||
                    lastModifiedAt >= cutoff)
                {
                    continue;
                }

                var deletion = await DeleteSessionCoreAsync(
                        sessionId,
                        includePinned: false,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (deletion.IsFailure)
                {
                    return Result<TemporaryOperationReport>.Failure(
                        deletion.Error);
                }

                report = report.Add(deletion.Value);
            }

            return Result<TemporaryOperationReport>.Success(report);
        }
        catch (Exception exception)
        {
            return Failure<TemporaryOperationReport>(
                exception,
                "Automatic temporary cleanup failed.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<TemporaryOperationReport>>
        DeleteCurrentNodeAsync(
            CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var enter = await EnterAsync(
                "Temporary node deletion was cancelled.",
                cancellationToken)
            .ConfigureAwait(false);

        if (enter.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                enter.Error);
        }

        try
        {
            var state = _pipeline.CurrentState;

            if (state is null)
            {
                return InvalidState(
                    "There is no active scan node.");
            }

            if (state.IsFiltering)
            {
                return InvalidState(
                    "A scan is running. Stop it before deleting " +
                    "temporary data.");
            }

            var active = state.ActiveRound;

            if (active.ParentRoundId is null)
            {
                return InvalidState(
                    "The root scan node cannot be deleted.");
            }

            if (state.Rounds.Any(round =>
                round.ParentRoundId == active.RoundId))
            {
                return InvalidState(
                    "Delete the node branch because the current node " +
                    "has descendants.");
            }

            if (active.IsPinned)
            {
                return InvalidState(
                    "Unpin the current scan node before deleting it.");
            }

            var bytes = GetFileLength(active.StorageReference);
            var clear = _cacheManager.Clear(
                active.Snapshot.SessionId);

            if (clear.IsFailure)
            {
                return Result<TemporaryOperationReport>.Failure(
                    clear.Error);
            }

            var undo = await _pipeline.UndoAsync(cancellationToken)
                .ConfigureAwait(false);

            if (undo.IsFailure)
            {
                return Result<TemporaryOperationReport>.Failure(
                    undo.Error);
            }

            var deletion = await _pipeline
                .DeletePendingRoundAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deletion.IsFailure)
            {
                return Result<TemporaryOperationReport>.Failure(
                    deletion.Error);
            }

            return Result<TemporaryOperationReport>.Success(
                new TemporaryOperationReport(
                    DeletedSnapshotCount: 1,
                    DeletedFileCount: 1,
                    ReclaimedBytes: bytes));
        }
        catch (Exception exception)
        {
            return Failure<TemporaryOperationReport>(
                exception,
                "The current temporary scan node could not be deleted.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<TemporaryOperationReport>>
        DeleteBranchAsync(
            Guid roundId,
            CancellationToken cancellationToken = default)
    {
        if (roundId == Guid.Empty)
        {
            return Validation("Round ID cannot be empty.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        var enter = await EnterAsync(
                "Temporary branch deletion was cancelled.",
                cancellationToken)
            .ConfigureAwait(false);

        if (enter.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                enter.Error);
        }

        try
        {
            var state = _pipeline.CurrentState;

            if (state is null)
            {
                return InvalidState(
                    "There is no active scan tree.");
            }

            if (state.IsFiltering)
            {
                return InvalidState(
                    "A scan is running. Stop it before deleting " +
                    "temporary data.");
            }

            var target = state.Rounds.FirstOrDefault(round =>
                round.RoundId == roundId);

            if (target is null)
            {
                return NotFound("The scan tree branch was not found.");
            }

            if (target.ParentRoundId is null)
            {
                return InvalidState(
                    "The root scan branch cannot be deleted.");
            }

            var subtree = GetSubtree(roundId, state.Rounds);
            var deleted = state.Rounds
                .Where(round => subtree.Contains(round.RoundId))
                .ToArray();

            if (deleted.Any(round => round.IsPinned))
            {
                return InvalidState(
                    "A pinned node prevents branch deletion.");
            }

            var reclaimedBytes = deleted.Sum(round =>
                GetFileLength(round.StorageReference));
            var clear = _cacheManager.Clear(
                target.Snapshot.SessionId);

            if (clear.IsFailure)
            {
                return Result<TemporaryOperationReport>.Failure(
                    clear.Error);
            }

            if (subtree.Contains(state.ActiveRound.RoundId))
            {
                var activateParent = await _pipeline
                    .SetActiveNodeAsync(
                        target.ParentRoundId.Value,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (activateParent.IsFailure)
                {
                    return Result<TemporaryOperationReport>.Failure(
                        activateParent.Error);
                }
            }

            var deletion = await _pipeline
                .DeleteBranchAsync(roundId, cancellationToken)
                .ConfigureAwait(false);

            if (deletion.IsFailure)
            {
                return Result<TemporaryOperationReport>.Failure(
                    deletion.Error);
            }

            return Result<TemporaryOperationReport>.Success(
                new TemporaryOperationReport(
                    DeletedSnapshotCount: deleted.Length,
                    DeletedFileCount: deleted.Length,
                    ReclaimedBytes: reclaimedBytes));
        }
        catch (Exception exception)
        {
            return Failure<TemporaryOperationReport>(
                exception,
                "The temporary scan branch could not be deleted.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Result<TemporaryOperationReport>> DeleteSessionAsync(
        Guid sessionId,
        bool includePinned = false,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Task.FromResult(
                Validation("Session ID cannot be empty."));
        }

        return RunExclusiveAsync(
            token => DeleteSessionCoreAsync(
                sessionId,
                includePinned,
                token),
            "Temporary session deletion was cancelled.",
            "The temporary session could not be deleted.",
            cancellationToken);
    }

    public Task<Result<TemporaryOperationReport>> DeleteAllAsync(
        bool includePinned = false,
        CancellationToken cancellationToken = default)
    {
        return RunExclusiveAsync(
            async token =>
            {
                var report = new TemporaryOperationReport();

                foreach (var (sessionId, _) in
                         EnumerateSessionDirectories())
                {
                    token.ThrowIfCancellationRequested();
                    var deletion = await DeleteSessionCoreAsync(
                            sessionId,
                            includePinned,
                            token)
                        .ConfigureAwait(false);

                    if (deletion.IsFailure)
                    {
                        return deletion;
                    }

                    report = report.Add(deletion.Value);
                }

                return Result<TemporaryOperationReport>.Success(
                    report);
            },
            "Deleting all temporary data was cancelled.",
            "Temporary data could not be deleted.",
            cancellationToken);
    }

    public Task<Result<TemporaryOperationReport>> CompactSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Task.FromResult(
                Validation("Session ID cannot be empty."));
        }

        return RunExclusiveAsync(
            token => CompactSessionCoreAsync(sessionId, token),
            "Temporary session compaction was cancelled.",
            "The temporary session could not be compacted.",
            cancellationToken);
    }

    public Result OpenTempFolder()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var ensure = _pathService.EnsureDirectories();

            if (ensure.IsFailure)
            {
                return ensure;
            }

            _ = Process.Start(
                new ProcessStartInfo
                {
                    FileName = _pathService.TempDirectory,
                    UseShellExecute = true,
                });
            return Result.Success();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Io,
                    "The temporary folder could not be opened.",
                    exception));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private async Task<Result<TemporaryOperationReport>>
        DeleteSessionCoreAsync(
            Guid sessionId,
            bool includePinned,
            CancellationToken cancellationToken)
    {
        var idle = EnsureScanIdle();

        if (idle.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                idle.Error);
        }

        var directory = GetSessionDirectory(sessionId);

        if (!Directory.Exists(directory))
        {
            return Result<TemporaryOperationReport>.Failure(
                new Error(
                    ErrorCode.NotFound,
                    "The temporary session was not found."));
        }

        var history = await _historyStore
            .LoadAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (!includePinned)
        {
            if (history.IsFailure)
            {
                return Result<TemporaryOperationReport>.Success(
                    new TemporaryOperationReport(
                        RetainedPinnedSessionCount: 1));
            }

            if (history.Value.Rounds.Any(round => round.IsPinned))
            {
                return Result<TemporaryOperationReport>.Success(
                    new TemporaryOperationReport(
                        RetainedPinnedSessionCount: 1));
            }
        }

        var before = GetDirectoryMetrics(directory);
        var clear = _cacheManager.Clear(sessionId);

        if (clear.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                clear.Error);
        }

        var close = _pipeline.CloseSession(sessionId);

        if (close.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                close.Error);
        }

        var descriptors = await OpenSessionSnapshotsAsync(
                sessionId,
                directory,
                cancellationToken)
            .ConfigureAwait(false);

        if (descriptors.IsFailure && !includePinned)
        {
            return Result<TemporaryOperationReport>.Failure(
                descriptors.Error);
        }

        var deletedSnapshots = 0;

        if (descriptors.IsSuccess)
        {
            foreach (var descriptor in descriptors.Value
                         .OrderByDescending(snapshot =>
                             snapshot.ChainDepth)
                         .ThenByDescending(snapshot =>
                             snapshot.NodeId))
            {
                var deletion = await _snapshotStorage.DeleteAsync(
                        sessionId,
                        descriptor.NodeId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (deletion.IsSuccess)
                {
                    deletedSnapshots++;
                    continue;
                }

                if (deletion.Error.Code != ErrorCode.NotFound &&
                    !includePinned)
                {
                    return Result<TemporaryOperationReport>.Failure(
                        deletion.Error);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(directory, recursive: true);
        return Result<TemporaryOperationReport>.Success(
            new TemporaryOperationReport(
                DeletedSessionCount: 1,
                DeletedSnapshotCount: deletedSnapshots,
                DeletedFileCount: before.FileCount,
                ReclaimedBytes: before.TotalBytes));
    }

    private async Task<Result<TemporaryOperationReport>>
        CompactSessionCoreAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
    {
        var idle = EnsureScanIdle();

        if (idle.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                idle.Error);
        }

        var directory = GetSessionDirectory(sessionId);

        if (!Directory.Exists(directory))
        {
            return Result<TemporaryOperationReport>.Failure(
                new Error(
                    ErrorCode.NotFound,
                    "The temporary session was not found."));
        }

        var clear = _cacheManager.Clear(sessionId);

        if (clear.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                clear.Error);
        }

        var recovery = await RecoverIncompleteCoreAsync(
                sessionId,
                directory,
                cancellationToken)
            .ConfigureAwait(false);

        if (recovery.IsFailure)
        {
            return recovery;
        }

        var history = await _historyStore
            .LoadAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (history.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                history.Error);
        }

        var referencedNodeIds = history.Value.Rounds
            .Select(round => round.SnapshotNodeId)
            .ToHashSet();
        var snapshots = await OpenSessionSnapshotsAsync(
                sessionId,
                directory,
                cancellationToken)
            .ConfigureAwait(false);

        if (snapshots.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                snapshots.Error);
        }

        var report = recovery.Value;

        foreach (var orphan in snapshots.Value
                     .Where(snapshot =>
                         !referencedNodeIds.Contains(snapshot.NodeId))
                     .OrderByDescending(snapshot =>
                         snapshot.ChainDepth)
                     .ThenByDescending(snapshot => snapshot.NodeId))
        {
            var bytes = GetFileLength(orphan.FilePath);
            var deletion = await _snapshotStorage.DeleteAsync(
                    sessionId,
                    orphan.NodeId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (deletion.IsFailure)
            {
                return Result<TemporaryOperationReport>.Failure(
                    deletion.Error);
            }

            report = report.Add(
                new TemporaryOperationReport(
                    DeletedSnapshotCount: 1,
                    DeletedFileCount: 1,
                    ReclaimedBytes: bytes));
        }

        foreach (var round in history.Value.Rounds)
        {
            var opened = await _snapshotStorage.OpenAsync(
                    sessionId,
                    round.SnapshotNodeId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (opened.IsFailure)
            {
                return Result<TemporaryOperationReport>.Failure(
                    opened.Error);
            }
        }

        var save = await _historyStore.SaveAsync(
                history.Value,
                cancellationToken)
            .ConfigureAwait(false);

        if (save.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                save.Error);
        }

        var verification = await _historyStore.LoadAsync(
                sessionId,
                cancellationToken)
            .ConfigureAwait(false);

        if (verification.IsFailure ||
            verification.Value.Rounds.Count !=
            history.Value.Rounds.Count)
        {
            return Result<TemporaryOperationReport>.Failure(
                verification.IsFailure
                    ? verification.Error
                    : new Error(
                        ErrorCode.Serialization,
                        "The compacted scan tree could not be verified."));
        }

        return Result<TemporaryOperationReport>.Success(
            report.Add(
                new TemporaryOperationReport(
                    CompactedSessionCount: 1)));
    }

    private async Task<Result<TemporaryOperationReport>>
        RecoverIncompleteCoreAsync(
            Guid sessionId,
            string directory,
            CancellationToken cancellationToken)
    {
        var recovery = await _snapshotStorage
            .RecoverIncompleteAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (recovery.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                recovery.Error);
        }

        var discarded = recovery.Value.DiscardedFileCount;
        var deletedFiles = 0;
        long reclaimedBytes = 0;

        foreach (var path in Directory
                     .EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly)
                     .Where(IsIncompletePath)
                     .ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            reclaimedBytes += GetFileLength(path);
            File.Delete(path);
            discarded++;
            deletedFiles++;
        }

        return Result<TemporaryOperationReport>.Success(
            new TemporaryOperationReport(
                DeletedFileCount: deletedFiles,
                ReclaimedBytes: reclaimedBytes,
                RecoveredFileCount:
                    recovery.Value.RecoveredFileCount,
                DiscardedIncompleteFileCount: discarded));
    }

    private async Task<TemporarySessionInfo> InspectSessionAsync(
        Guid sessionId,
        string directory,
        Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        var metrics = GetDirectoryMetrics(directory);
        var history = await _historyStore
            .LoadAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        var files = Directory.EnumerateFiles(
                directory,
                "*",
                SearchOption.TopDirectoryOnly)
            .ToArray();
        return new TemporarySessionInfo(
            sessionId,
            metrics.TotalBytes,
            metrics.FileCount,
            files.Count(IsSnapshotPath),
            files.Count(IsIncompletePath),
            history.IsSuccess
                ? history.Value.Rounds.Count(round =>
                    round.IsPinned)
                : 0,
            new DateTimeOffset(
                Directory.GetLastWriteTimeUtc(directory)),
            history.IsSuccess,
            sessionId == currentSessionId);
    }

    private async Task<Result<IReadOnlyList<SnapshotDescriptor>>>
        OpenSessionSnapshotsAsync(
            Guid sessionId,
            string directory,
            CancellationToken cancellationToken)
    {
        var nodeIds = new HashSet<int>();

        foreach (var path in Directory.EnumerateFiles(
            directory,
            "node_*.bin",
            SearchOption.TopDirectoryOnly))
        {
            var match = SnapshotFileNameRegex().Match(
                Path.GetFileName(path));

            if (match.Success &&
                int.TryParse(
                    match.Groups["node"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var nodeId) &&
                nodeId > 0)
            {
                nodeIds.Add(nodeId);
            }
        }

        var snapshots = new List<SnapshotDescriptor>(
            nodeIds.Count);

        foreach (var nodeId in nodeIds)
        {
            var opened = await _snapshotStorage.OpenAsync(
                    sessionId,
                    nodeId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (opened.IsFailure)
            {
                return Result<IReadOnlyList<SnapshotDescriptor>>
                    .Failure(opened.Error);
            }

            snapshots.Add(opened.Value);
        }

        return Result<IReadOnlyList<SnapshotDescriptor>>.Success(
            snapshots.AsReadOnly());
    }

    private async Task<Result<TemporaryOperationReport>>
        RunExclusiveAsync(
            Func<CancellationToken,
                Task<Result<TemporaryOperationReport>>> operation,
            string cancellationMessage,
            string failureMessage,
            CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var enter = await EnterAsync(
                cancellationMessage,
                cancellationToken)
            .ConfigureAwait(false);

        if (enter.IsFailure)
        {
            return Result<TemporaryOperationReport>.Failure(
                enter.Error);
        }

        try
        {
            return await operation(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Failure<TemporaryOperationReport>(
                exception,
                failureMessage,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result> EnterAsync(
        string cancellationMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException exception)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    cancellationMessage,
                    exception));
        }
    }

    private Result EnsureScanIdle()
    {
        return _pipeline.CurrentState?.IsFiltering == true ||
               _snapshotStorage.IsOperationInProgress
            ? Result.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "A scan or snapshot operation is running. Stop it " +
                    "before managing temporary data."))
            : Result.Success();
    }

    private Guid? GetCurrentSessionId()
    {
        return _pipeline.CurrentState?
            .ActiveRound.Snapshot.SessionId;
    }

    private IEnumerable<(Guid SessionId, string Directory)>
        EnumerateSessionDirectories()
    {
        if (!Directory.Exists(_pathService.TempDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(
                _pathService.TempDirectory,
                "*",
                SearchOption.TopDirectoryOnly)
            .Select(path => (
                Parsed: Guid.TryParse(
                    Path.GetFileName(path),
                    out var sessionId),
                SessionId: sessionId,
                Directory: path))
            .Where(entry =>
                entry.Parsed &&
                IsWithinTempRoot(entry.Directory))
            .Select(entry =>
                (entry.SessionId, entry.Directory))
            .ToArray();
    }

    private string GetSessionDirectory(Guid sessionId)
    {
        var path = Path.GetFullPath(
            Path.Combine(
                _pathService.TempDirectory,
                sessionId.ToString("D")));

        if (!IsWithinTempRoot(path))
        {
            throw new InvalidOperationException(
                "The temporary session path escaped the temp root.");
        }

        return path;
    }

    private bool IsWithinTempRoot(string path)
    {
        var root = Path.GetFullPath(_pathService.TempDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(
            root,
            StringComparison.OrdinalIgnoreCase);
    }

    private static (long TotalBytes, int FileCount)
        GetDirectoryMetrics(string directory)
    {
        long bytes = 0;
        var count = 0;

        foreach (var path in Directory.EnumerateFiles(
            directory,
            "*",
            SearchOption.AllDirectories))
        {
            count++;
            bytes = checked(bytes + GetFileLength(path));
        }

        return (bytes, count);
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return File.Exists(path)
                ? new FileInfo(path).Length
                : 0;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return 0;
        }
    }

    private static bool IsSnapshotPath(string path)
    {
        return SnapshotFileNameRegex().IsMatch(
            Path.GetFileName(path));
    }

    private static bool IsIncompletePath(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains(".tmp-", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<Guid> GetSubtree(
        Guid rootId,
        IReadOnlyList<FilterPipelineRound> rounds)
    {
        var result = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(rootId);

        while (pending.TryPop(out var current))
        {
            if (!result.Add(current))
            {
                continue;
            }

            foreach (var child in rounds.Where(round =>
                round.ParentRoundId == current))
            {
                pending.Push(child.RoundId);
            }
        }

        return result;
    }

    private static Result<TemporaryOperationReport> Validation(
        string message)
    {
        return Result<TemporaryOperationReport>.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private static Result<TemporaryOperationReport> InvalidState(
        string message)
    {
        return Result<TemporaryOperationReport>.Failure(
            new Error(ErrorCode.InvalidState, message));
    }

    private static Result<TemporaryOperationReport> NotFound(
        string message)
    {
        return Result<TemporaryOperationReport>.Failure(
            new Error(ErrorCode.NotFound, message));
    }

    private static Result<T> Failure<T>(
        Exception exception,
        string message,
        CancellationToken cancellationToken)
    {
        return exception switch
        {
            OperationCanceledException
                when cancellationToken.IsCancellationRequested =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.Cancelled,
                        message,
                        exception)),
            IOException or
            UnauthorizedAccessException or
            NotSupportedException =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.Io,
                        message,
                        exception)),
            InvalidDataException or
            InvalidOperationException or
            ArgumentException =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        message,
                        exception)),
            _ => Result<T>.Failure(
                new Error(
                    ErrorCode.Unexpected,
                    message,
                    exception)),
        };
    }

    [GeneratedRegex(
        @"^node_(?<node>[0-9]+)\.(?:full|keep\.delta|remove\.delta)\.bin$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SnapshotFileNameRegex();
}

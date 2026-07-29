using MemoryInspector.Application.Logging;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.Monitoring;

public sealed class MonitoringSessionService : IMonitoringSessionService
{
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly IMonitoringTargetConnectionFactory _connectionFactory;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _livenessCheckInterval;
    private IMonitoringTargetConnection? _connection;
    private MonitoringSession? _currentSession;
    private CancellationTokenSource? _livenessCancellation;
    private Task? _livenessTask;
    private bool _disposed;

    public MonitoringSessionService(
        IMonitoringTargetConnectionFactory connectionFactory,
        IAppLogger logger,
        TimeProvider timeProvider,
        TimeSpan livenessCheckInterval)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
        _timeProvider = Guard.NotNull(timeProvider);

        if (livenessCheckInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(livenessCheckInterval),
                livenessCheckInterval,
                "Liveness check interval must be greater than zero.");
        }

        _livenessCheckInterval = livenessCheckInterval;
    }

    public MonitoringSession? CurrentSession =>
        Volatile.Read(ref _currentSession);

    public event EventHandler<MonitoringSessionChangedEventArgs>? SessionChanged;

    public async Task<Result<MonitoringSession>> StartAsync(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Guard.NotNull(identity);

        MonitoringSession connectingSession;

        try
        {
            await _sessionGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return CancelledSessionResult(
                "Starting the monitoring session was cancelled.",
                exception);
        }

        try
        {
            if (_currentSession?.IsActive == true)
            {
                if (_currentSession.State == MonitoringSessionState.Connected &&
                    _currentSession.Identity == identity)
                {
                    return Result<MonitoringSession>.Success(_currentSession);
                }

                return Result<MonitoringSession>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "Another monitoring session is already active."));
            }

            connectingSession = new MonitoringSession
            {
                SessionId = Guid.NewGuid(),
                Identity = identity,
                State = MonitoringSessionState.Connecting,
                CreatedAt = _timeProvider.GetUtcNow(),
                StatusMessage =
                    $"Connecting to {identity.ProcessName} " +
                    $"(PID {identity.ProcessId}).",
            };
            _currentSession = connectingSession;
        }
        finally
        {
            _sessionGate.Release();
        }

        Publish(connectingSession);

        Result<IMonitoringTargetConnection> connectionResult;

        try
        {
            connectionResult = await _connectionFactory.ConnectAsync(
                identity,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            connectionResult = Result<IMonitoringTargetConnection>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Starting the monitoring session was cancelled.",
                    exception));
        }
        catch (Exception exception)
        {
            connectionResult = Result<IMonitoringTargetConnection>.Failure(
                new Error(
                    ErrorCode.Unexpected,
                    "The monitoring target connection could not be created.",
                    exception));
        }

        try
        {
            await _sessionGate.WaitAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            if (connectionResult.IsSuccess)
            {
                await connectionResult.Value.DisposeAsync();
            }

            return Result<MonitoringSession>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "The monitoring session service has been disposed."));
        }

        MonitoringSession? completedSession = null;
        IMonitoringTargetConnection? abandonedConnection = null;
        Error? supersededError = null;

        try
        {
            if (_currentSession?.SessionId != connectingSession.SessionId ||
                _currentSession.State != MonitoringSessionState.Connecting)
            {
                abandonedConnection = connectionResult.IsSuccess
                    ? connectionResult.Value
                    : null;
                supersededError = new Error(
                    ErrorCode.InvalidState,
                    "The monitoring request was superseded before it completed.");
            }
            else if (connectionResult.IsFailure)
            {
                completedSession = connectingSession with
                {
                    State = MapFailureState(connectionResult.Error),
                    EndedAt = _timeProvider.GetUtcNow(),
                    StatusMessage = connectionResult.Error.ToDisplayMessage(),
                };
                _currentSession = completedSession;
            }
            else if (connectionResult.Value.Identity != identity)
            {
                abandonedConnection = connectionResult.Value;
                completedSession = connectingSession with
                {
                    State = MonitoringSessionState.Invalidated,
                    EndedAt = _timeProvider.GetUtcNow(),
                    StatusMessage =
                        "The connected process identity does not match the request.",
                };
                _currentSession = completedSession;
            }
            else
            {
                _connection = connectionResult.Value;
                completedSession = connectingSession with
                {
                    State = MonitoringSessionState.Connected,
                    ConnectedAt = _timeProvider.GetUtcNow(),
                    StatusMessage =
                        $"Monitoring {identity.ProcessName} " +
                        $"(PID {identity.ProcessId}).",
                };
                _currentSession = completedSession;
                StartLivenessMonitor(completedSession.SessionId);
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        if (abandonedConnection is not null)
        {
            await abandonedConnection.DisposeAsync();
        }

        if (supersededError is not null)
        {
            return Result<MonitoringSession>.Failure(supersededError);
        }

        var publishedSession = completedSession!;
        Publish(publishedSession);

        if (connectionResult.IsFailure)
        {
            _ = _logger.Log(
                AppLogLevel.Warning,
                publishedSession.StatusMessage ??
                "The monitoring session could not be started.",
                connectionResult.Error.Exception);

            return Result<MonitoringSession>.Failure(connectionResult.Error);
        }

        if (publishedSession.State == MonitoringSessionState.Invalidated)
        {
            return Result<MonitoringSession>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    publishedSession.StatusMessage!));
        }

        return Result<MonitoringSession>.Success(publishedSession);
    }

    public Task<Result<MonitoringSession>> CheckLivenessAsync(
        CancellationToken cancellationToken = default)
    {
        var sessionId = CurrentSession?.SessionId;

        return sessionId.HasValue
            ? CheckLivenessAsync(sessionId.Value, cancellationToken)
            : Task.FromResult(
                Result<MonitoringSession>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "There is no monitoring session to check.")));
    }

    public async Task<Result> StopAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _sessionGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Stopping the monitoring session was cancelled.",
                    exception));
        }

        IMonitoringTargetConnection? connection;
        MonitoringSession? stoppedSession = null;

        try
        {
            CancelLivenessMonitor();
            connection = _connection;
            _connection = null;

            if (_currentSession is not null &&
                _currentSession.State != MonitoringSessionState.Disconnected)
            {
                stoppedSession = _currentSession with
                {
                    State = MonitoringSessionState.Disconnected,
                    EndedAt = _timeProvider.GetUtcNow(),
                    StatusMessage = "Monitoring session stopped.",
                };
                _currentSession = stoppedSession;
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }

        if (stoppedSession is not null)
        {
            Publish(stoppedSession);
        }

        return Result.Success();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None);
        _disposed = true;
        _sessionGate.Dispose();
    }

    private async Task<Result<MonitoringSession>> CheckLivenessAsync(
        Guid expectedSessionId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _sessionGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return CancelledSessionResult(
                "The target liveness check was cancelled.",
                exception);
        }

        IMonitoringTargetConnection? connectionToDispose = null;
        MonitoringSession? changedSession = null;
        Result<bool>? livenessResult = null;

        try
        {
            if (_currentSession?.SessionId != expectedSessionId ||
                _currentSession.State != MonitoringSessionState.Connected ||
                _connection is null)
            {
                return Result<MonitoringSession>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "The monitoring session is no longer connected."));
            }

            livenessResult = await _connection.IsAliveAsync(cancellationToken);

            if (livenessResult.IsSuccess && livenessResult.Value)
            {
                return Result<MonitoringSession>.Success(_currentSession);
            }

            CancelLivenessMonitor();
            connectionToDispose = _connection;
            _connection = null;
            changedSession = _currentSession with
            {
                State = livenessResult.IsSuccess
                    ? MonitoringSessionState.TargetExited
                    : MapFailureState(livenessResult.Error),
                EndedAt = _timeProvider.GetUtcNow(),
                StatusMessage = livenessResult.IsSuccess
                    ? "The target process has exited."
                    : livenessResult.Error.ToDisplayMessage(),
            };
            _currentSession = changedSession;
        }
        finally
        {
            _sessionGate.Release();
        }

        if (connectionToDispose is not null)
        {
            await connectionToDispose.DisposeAsync();
        }

        Publish(changedSession!);

        if (livenessResult!.IsFailure)
        {
            _ = _logger.Log(
                AppLogLevel.Warning,
                changedSession!.StatusMessage!,
                livenessResult.Error.Exception);
            return Result<MonitoringSession>.Failure(livenessResult.Error);
        }

        return Result<MonitoringSession>.Success(changedSession!);
    }

    private void StartLivenessMonitor(Guid sessionId)
    {
        CancelLivenessMonitor();
        _livenessCancellation = new CancellationTokenSource();
        _livenessTask = RunLivenessMonitorAsync(
            sessionId,
            _livenessCancellation.Token);
    }

    private void CancelLivenessMonitor()
    {
        _livenessCancellation?.Cancel();
        _livenessCancellation?.Dispose();
        _livenessCancellation = null;
        _livenessTask = null;
    }

    private async Task RunLivenessMonitorAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                    _livenessCheckInterval,
                    cancellationToken);
                var result = await CheckLivenessAsync(
                    sessionId,
                    cancellationToken);

                if (result.IsFailure ||
                    result.Value.State != MonitoringSessionState.Connected)
                {
                    return;
                }
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
            _ = _logger.Log(
                AppLogLevel.Error,
                "The target liveness monitor stopped unexpectedly.",
                exception);
        }
    }

    private void Publish(MonitoringSession session)
    {
        SessionChanged?.Invoke(
            this,
            new MonitoringSessionChangedEventArgs(session));
    }

    private static MonitoringSessionState MapFailureState(Error error)
    {
        return error.Code switch
        {
            ErrorCode.AccessDenied => MonitoringSessionState.AccessDenied,
            ErrorCode.NotFound => MonitoringSessionState.TargetExited,
            ErrorCode.InvalidState => MonitoringSessionState.Invalidated,
            ErrorCode.Cancelled => MonitoringSessionState.Disconnected,
            _ => MonitoringSessionState.Error,
        };
    }

    private static Result<MonitoringSession> CancelledSessionResult(
        string message,
        OperationCanceledException exception)
    {
        return Result<MonitoringSession>.Failure(
            new Error(
                ErrorCode.Cancelled,
                message,
                exception));
    }
}

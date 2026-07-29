namespace MemoryInspector.Application.Scanning;

public sealed class DurationFilterExecutionControl
{
    private readonly object _sync = new();
    private bool _isPaused;
    private TaskCompletionSource _stateChanged = CreateSignal();
    private CancellationTokenSource _pauseRequested = new();

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _isPaused;
            }
        }
    }

    public void Pause()
    {
        SetPaused(isPaused: true);
    }

    public void Resume()
    {
        SetPaused(isPaused: false);
    }

    internal StateSnapshot CaptureState()
    {
        lock (_sync)
        {
            return new StateSnapshot(
                _isPaused,
                _stateChanged.Task,
                _pauseRequested.Token);
        }
    }

    private void SetPaused(bool isPaused)
    {
        TaskCompletionSource signal;
        CancellationTokenSource? pauseRequested = null;

        lock (_sync)
        {
            if (_isPaused == isPaused)
            {
                return;
            }

            _isPaused = isPaused;
            signal = _stateChanged;
            _stateChanged = CreateSignal();

            if (isPaused)
            {
                pauseRequested = _pauseRequested;
            }
            else
            {
                _pauseRequested = new CancellationTokenSource();
            }
        }

        pauseRequested?.Cancel();
        signal.TrySetResult();
    }

    private static TaskCompletionSource CreateSignal()
    {
        return new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal readonly record struct StateSnapshot(
        bool IsPaused,
        Task StateChanged,
        CancellationToken PauseRequested);
}

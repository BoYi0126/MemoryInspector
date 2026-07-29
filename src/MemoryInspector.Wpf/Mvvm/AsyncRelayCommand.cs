using System.Windows.Input;

namespace MemoryInspector.Wpf.Mvvm;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly bool _allowConcurrentExecutions;
    private int _executionCount;

    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        bool allowConcurrentExecutions = false)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _allowConcurrentExecutions = allowConcurrentExecutions;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return (_allowConcurrentExecutions ||
                Volatile.Read(ref _executionCount) == 0) &&
               (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync();
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        Interlocked.Increment(ref _executionCount);
        NotifyCanExecuteChanged();

        try
        {
            await _execute();
        }
        finally
        {
            Interlocked.Decrement(ref _executionCount);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

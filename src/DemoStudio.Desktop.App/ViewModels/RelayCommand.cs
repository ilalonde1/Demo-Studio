using System.Diagnostics;
using System.Threading;
using System.Windows.Input;
namespace DemoStudio.Desktop.App.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?>? _execute;
    private readonly Func<object?, Task>? _executeAsync;
    private readonly Predicate<object?>? _canExecute;
    private readonly bool _allowConcurrentExecution;
    private int _isExecuting;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null, bool allowConcurrentExecution = true)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _allowConcurrentExecution = allowConcurrentExecution;
    }

    public RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null, bool allowConcurrentExecution = false)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
        _allowConcurrentExecution = allowConcurrentExecution;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (!_allowConcurrentExecution && _executeAsync is not null && Volatile.Read(ref _isExecuting) != 0)
        {
            return false;
        }

        return _canExecute?.Invoke(parameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        if (_executeAsync is null)
        {
            _execute!(parameter);
            return;
        }

        _ = ExecuteAsync(parameter);
    }

    private async Task ExecuteAsync(object? parameter)
    {
        if (!_allowConcurrentExecution)
        {
            if (Interlocked.Exchange(ref _isExecuting, 1) == 1)
            {
                return;
            }

            RaiseCanExecuteChanged();
        }

        try
        {
            await _executeAsync!(parameter);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(
                $"RelayCommand async execution failed: {ex}");
        }
        finally
        {
            if (!_allowConcurrentExecution)
            {
                Interlocked.Exchange(ref _isExecuting, 0);
                RaiseCanExecuteChanged();
            }
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

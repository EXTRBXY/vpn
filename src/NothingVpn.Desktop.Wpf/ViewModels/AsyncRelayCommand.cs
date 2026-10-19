using System.Windows.Input;

namespace NothingVpn.Desktop.Wpf;

internal sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    // Ошибка из async void уходит в SynchronizationContext диспетчера и попадает в глобальный обработчик
    // (журнал + сообщение), а не теряется.
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        RaiseCanExecuteChanged();
        try { await _execute(); }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

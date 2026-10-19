using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    private readonly IConnectionDiagnosticController _controller;
    private readonly ILogPort _log;
    private readonly Func<ConnectionMode> _mode;
    private readonly Func<bool> _running;
    private string _result = "";
    private string _logText = "";
    private int? _shownLogVersion;

    public DiagnosticsViewModel(
        IConnectionDiagnosticController controller,
        ILogPort log,
        Func<ConnectionMode> mode,
        Func<bool> running)
    {
        _controller = controller;
        _log = log;
        _mode = mode;
        _running = running;
        RunCommand = new AsyncRelayCommand(RunAsync);
        ClearCommand = new RelayCommand(() =>
        {
            _log.Clear();
            Refresh();
        });
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RunCommand { get; }
    public ICommand ClearCommand { get; }

    public string Result
    {
        get => _result;
        private set
        {
            _result = value;
            OnChanged();
        }
    }

    public string LogText
    {
        get => _logText;
        private set
        {
            _logText = value;
            OnChanged();
        }
    }

    public void ShowError(string message) => Result = message;

    private async Task RunAsync()
    {
        try
        {
            Result = "Проверка связности…";
            var result = await _controller.RunAsync(_mode(), _running());
            Result = result.Message;
        }
        catch (Exception ex)
        {
            Result = ex.Message;
        }
    }

    private void Refresh()
    {
        // Версия читается до снимка: снимок не старее версии, а пропущенная запись догонится на следующем тике.
        var version = _log.Version;
        if (version == _shownLogVersion) return;
        _shownLogVersion = version;
        var text = _log.SnapshotText(LogLevel.Trace);
        if (text != LogText) LogText = text;
    }

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

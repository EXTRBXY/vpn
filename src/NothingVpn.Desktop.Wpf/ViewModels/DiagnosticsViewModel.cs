using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

/// <summary>
/// Проверка связности и журнал. Фильтр уровня — только вид: в буфере остаются все записи. Выбор живёт, пока открыт
/// клиент (как в прежнем WinForms-клиенте), и в state.json не пишется. «Копировать» берёт то, что видно с фильтром,
/// «Сохранить» — весь журнал.
/// </summary>
public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    public const LogLevel DefaultMinLevel = LogLevel.Info;

    private readonly IConnectionDiagnosticController _controller;
    private readonly ILogPort _log;
    private readonly Func<ConnectionMode> _mode;
    private readonly Func<bool> _running;
    private string _result = "";
    private string _logText = "";
    private int? _shownLogVersion;
    private LogLevel _minLevel = DefaultMinLevel;

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

    /// <summary>Минимальный показываемый уровень; <see cref="LogLevel.Trace"/> — все записи.</summary>
    public LogLevel MinLevel
    {
        get => _minLevel;
        set
        {
            if (_minLevel == value) return;
            _minLevel = value;
            OnChanged();
            _shownLogVersion = null;
            Refresh();
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

    /// <summary>Весь журнал без фильтра уровня (записи уже замаскированы при добавлении).</summary>
    public string FullLogText() => _log.SnapshotText(LogLevel.Trace);

    public void SaveFullLog(string path, string text)
    {
        File.WriteAllText(path, text);
        _log.Append(LogLevel.Info, AppLogSources.Ui, $"Журнал сохранён в файл {Path.GetFileName(path)}.");
    }

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
        var text = _log.SnapshotText(_minLevel);
        if (text != LogText) LogText = text;
    }

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

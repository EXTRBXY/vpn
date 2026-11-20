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
/// «Сохранить» — весь журнал. Архив между подключениями включается отдельно и в state.json не пишется.
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
    private string _persistentLogInfo = "";
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

    public bool PersistentLoggingEnabled
    {
        get => _log.PersistentLoggingEnabled;
        set
        {
            if (value == _log.PersistentLoggingEnabled) return;
            try { _log.SetPersistentLogging(value); }
            catch (Exception ex) { Result = $"Не удалось изменить сохранение журнала: {ex.Message}"; }
            OnChanged();
            UpdatePersistentLogInfo();
        }
    }

    public string PersistentLogInfo => _persistentLogInfo;

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
        UpdatePersistentLogInfo();
        var version = _log.Version;
        if (version == _shownLogVersion) return;
        _shownLogVersion = version;
        var text = _log.SnapshotText(_minLevel);
        if (text != LogText) LogText = text;
    }

    private void UpdatePersistentLogInfo()
    {
        var directory = _log.PersistentLogDirectory;
        string info;
        if (_log.PersistentLogError is { } error)
            info = directory is null
                ? $"Ошибка сохранения журнала: {error}"
                : $"Ошибка сохранения журнала: {error} Каталог: {directory}.";
        else if (!_log.PersistentLoggingEnabled)
            info = directory is null
                ? "Сохранение журнала между подключениями выключено."
                : $"Выключено. Включение пишет журнал в {directory}.";
        else
            info = $"Журнал пишется в {directory}. В файлах могут быть адреса и имена сайтов — проверьте их перед передачей. Пока приложение закрыто, новые события не сохраняются. Кнопка «Очистить» эти файлы не удаляет.";

        if (info == _persistentLogInfo)
            return;
        _persistentLogInfo = info;
        OnChanged(nameof(PersistentLogInfo));
    }

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

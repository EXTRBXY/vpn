using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

public sealed class UpdateViewModel : INotifyPropertyChanged
{
    private readonly IAppUpdateController _controller;
    private readonly IInstallerUpdateService _installer;
    private readonly IInstallerLaunchService _launcher;
    private readonly ISettingsService _settings;
    private readonly ILogPort _log;
    private readonly Action<Action> _exitThenLaunch;
    private readonly Action<AppReleaseModel>? _notifyAvailable;
    private readonly Action<ReleaseChangelog>? _showChangelog;
    private readonly AsyncRelayCommand _checkCommand;
    private readonly AsyncRelayCommand _installCommand;
    private AppStateModel _state;
    private AppReleaseModel? _release;
    private string _status = "";
    private int _progress;
    private int _operationRunning;

    public UpdateViewModel(
        IAppUpdateController controller,
        IInstallerUpdateService installer,
        IInstallerLaunchService launcher,
        ISettingsService settings,
        AppStateModel state,
        ILogPort log,
        Action<Action> exitThenLaunch,
        Action<AppReleaseModel>? notifyAvailable = null,
        Action<ReleaseChangelog>? showChangelog = null)
    {
        _controller = controller;
        _installer = installer;
        _launcher = launcher;
        _settings = settings;
        _state = state;
        _log = log;
        _exitThenLaunch = exitThenLaunch;
        _notifyAvailable = notifyAvailable;
        _showChangelog = showChangelog;
        _checkCommand = new AsyncRelayCommand(CheckAsync, () => _operationRunning == 0);
        _installCommand = new AsyncRelayCommand(InstallAsync, () => _release is not null && _operationRunning == 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CheckCommand => _checkCommand;
    public ICommand InstallCommand => _installCommand;
    public string CurrentVersion => GetVersion();

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Changed();
        }
    }

    public int Progress
    {
        get => _progress;
        private set
        {
            _progress = value;
            Changed();
        }
    }

    public async Task CheckAsync()
    {
        if (Interlocked.Exchange(ref _operationRunning, 1) == 1) return;
        RaiseCommands();
        try
        {
            Status = "Проверка…";
            _state = _settings.GetState();
            var result = await _controller.CheckAsync(_state, GetVersion());
            _release = result.AvailableRelease;
            Status = !result.Succeeded
                ? "Не удалось проверить обновления."
                : _release is null
                    ? "Установлена актуальная версия."
                    : $"Доступна версия {_release.Semver}.";
            if (!result.Succeeded)
                Log(LogLevel.Warn, "Не удалось проверить обновления" + (string.IsNullOrWhiteSpace(result.Error) ? "." : ": " + result.Error));
            else
                Log(LogLevel.Info, "Проверка обновлений: " + Status);
            _installCommand.RaiseCanExecuteChanged();
            if (_release is not null && _notifyAvailable is not null && _controller.ShouldOffer(_state, _release))
            {
                _notifyAvailable(_release);
                _controller.DismissOffer(_state, _release);
            }
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log(LogLevel.Warn, "Не удалось проверить обновления: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _operationRunning, 0);
            RaiseCommands();
        }
    }

    public async Task CheckIfDueAsync()
    {
        try
        {
            _state = _settings.GetState();
            if (_controller.IsPeriodicCheckDue(_state, DateTimeOffset.UtcNow))
                await CheckAsync();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log(LogLevel.Warn, "Не удалось проверить обновления: " + ex.Message);
        }
    }

    public async Task CheckAtStartupAsync()
    {
        try
        {
            _installer.CleanupOldInstallers();
            _state = _settings.GetState();
            var version = GetVersion();
            var transition = _controller.RecordInstalledVersion(_state, version);
            if (transition == InstalledVersionTransition.Upgraded)
            {
                Log(LogLevel.Info, $"Приложение обновлено до версии {version}.");
                await ShowChangelogAsync(version);
            }
            await CheckIfDueAsync();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log(LogLevel.Warn, "Проверка обновлений при запуске не удалась: " + ex.Message);
        }
    }

    private async Task ShowChangelogAsync(string version)
    {
        if (_showChangelog is null)
            return;

        // Описание релиза — вспомогательная информация: сбой загрузки или показа не должен мешать проверке обновлений.
        try
        {
            var changelog = await _controller.GetChangelogAsync(version);
            if (changelog.LoadFailed)
                Log(LogLevel.Warn, "Не удалось загрузить описание релиза.");
            _showChangelog(changelog);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Warn, "Не удалось показать описание релиза: " + ex.Message);
        }
    }

    private async Task InstallAsync()
    {
        if (_release is null || Interlocked.Exchange(ref _operationRunning, 1) == 1) return;
        var release = _release;
        RaiseCommands();
        Progress = 0;
        Status = "Загрузка обновления…";
        Log(LogLevel.Info, $"Загрузка обновления {release.Semver}.");
        try
        {
            _launcher.EnsureLaunchAllowed();
            var progress = new Progress<InstallerDownloadProgressModel>(x =>
                Progress = x.TotalBytes is > 0 ? (int)(x.BytesReceived * 100 / x.TotalBytes.Value) : 0);
            var download = await _installer.DownloadAsync(release, progress);
            if (!download.Success || string.IsNullOrWhiteSpace(download.InstallerPath))
            {
                Status = download.Error ?? "Не удалось загрузить обновление.";
                Log(LogLevel.Warn, "Не удалось загрузить обновление: " + Status);
                return;
            }
            // Проверки — до выхода: их ошибка не отключает VPN. Запуск — после выхода, см. IInstallerLaunchService.
            var installerPath = _launcher.Prepare(download.InstallerPath);
            Status = "Закрытие приложения и запуск установщика…";
            Log(LogLevel.Info, "Обновление загружено: приложение закроется и запустит установщик.");
            _exitThenLaunch(() => _launcher.Launch(installerPath));
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log(LogLevel.Error, "Не удалось установить обновление: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _operationRunning, 0);
            RaiseCommands();
        }
    }

    private void Log(LogLevel level, string message)
    {
        try { _log.Append(level, AppLogSources.Update, message); }
        catch { }
    }

    private void RaiseCommands()
    {
        _checkCommand.RaiseCanExecuteChanged();
        _installCommand.RaiseCanExecuteChanged();
    }

    private static string GetVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

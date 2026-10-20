using System.IO;
using NothingVpn.Application.Ports;
using NothingVpn.Desktop.Wpf.Composition;
using NothingVpn.Domain.Models;

namespace NothingVpn.Desktop.Wpf;

/// <summary>Старт, выход и связывание частей клиента. Сборка зависимостей — <see cref="DesktopCompositionRoot"/>.</summary>
public partial class App : System.Windows.Application
{
    private const string AppId = "NothingVpn.Desktop.Wpf";
    private SystemThemeWatcher? _theme;
    private SingleInstance? _singleInstance;
    private GlobalExceptionHandler? _exceptions;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private UpdateCheckScheduler? _updateChecks;
    private SubscriptionRefreshScheduler? _subscriptionRefresh;
    private ILogPort? _log;
    private bool _exitInProgress;

    public static bool IsExitRequested { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        _theme = new SystemThemeWatcher(Dispatcher);
        _theme.Start();
        var smokeTest = StartupOptions.HasSwitch(e.Args, StartupOptions.SmokeTestSwitch);
        var smokeErrorPath = Path.Combine(AppContext.BaseDirectory, "wpf-smoke-error.txt");
        if (smokeTest)
        {
            try { File.Delete(smokeErrorPath); }
            catch { }
        }

        _singleInstance = SingleInstance.Acquire(AppId, e.Args);
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        try
        {
            var composition = DesktopCompositionRoot.Create(new DesktopShell(
                RequestExit,
                ExitThenLaunch,
                release => _tray?.ShowUpdateAvailable(release),
                changelog => AppDialogs.ShowReleaseChangelog(_window, changelog)));
            var exceptions = new GlobalExceptionHandler(
                composition.Log,
                message =>
                {
                    if (!IsExitRequested) AppDialogs.ShowUnexpectedError(_window, message);
                },
                () => composition.Connection.StopAsync());
            exceptions.Attach(this);
            _exceptions = exceptions;
            _log = composition.Log;
            composition.Log.Append(LogLevel.Info, AppLogSources.App, $"Nothing VPN {composition.Updates.CurrentVersion} запущен.");

            var viewModel = composition.Main;
            _viewModel = viewModel;
            viewModel.ConnectionStateChanged += (_, connected) => _tray?.SetConnected(connected);
            _window = new MainWindow(viewModel);
            MainWindow = _window;
            _tray = new TrayIcon(
                Dispatcher,
                ShowMainWindow,
                viewModel.ConnectFromTrayAsync,
                viewModel.DisconnectFromTrayAsync,
                RequestExit,
                ex => exceptions.HandleUiException(ex));
            _tray.SetConnected(composition.Connection.IsRunning);
            _window.Show();
            if (smokeTest)
            {
                Dispatcher.BeginInvoke(ExitSmokeTest);
                return;
            }

            AppDialogs.ShowStorageIssues(_window, composition.StorageHealth);
            _updateChecks = new UpdateCheckScheduler(composition.Updates);
            _updateChecks.Start();
            viewModel.ApplyStartupAsync(StartupOptions.Parse(e.Args)).Forget();
            _subscriptionRefresh = new SubscriptionRefreshScheduler(
                composition.SubscriptionService,
                composition.Subscriptions,
                composition.Log);
            _subscriptionRefresh.Start();

            _singleInstance.StartServer(args => Dispatcher.Invoke(async () =>
            {
                ShowMainWindow();
                await viewModel.ApplyStartupAsync(StartupOptions.Parse(args));
            }));
        }
        catch (Exception ex)
        {
            if (smokeTest)
            {
                Console.Error.WriteLine(ex);
                try { File.WriteAllText(smokeErrorPath, ex.ToString()); }
                catch { }
                Shutdown(1);
                return;
            }
            AppDialogs.ShowStartupError(ex.Message);
            RequestExit();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _updateChecks?.Stop();
        _theme?.Dispose();
        base.OnExit(e);
    }

    private void ShowMainWindow() => _window?.BringToFront();

    private void ExitSmokeTest()
    {
        IsExitRequested = true;
        _tray?.Dispose();
        _singleInstance?.Dispose();
        _window?.Close();
        Shutdown(0);
    }

    private void RequestExit() => ExitThenLaunch(null);

    /// <summary>
    /// Штатный выход. <paramref name="launchAfterRelease"/> (запуск установщика обновления) выполняется после
    /// отключения VPN и освобождения мьютекса: установщик Inno с <c>AppMutex</c> в тихом режиме не ждёт, а
    /// прерывается, если застаёт интерфейс работающим.
    /// </summary>
    private async void ExitThenLaunch(Action? launchAfterRelease)
    {
        if (_exitInProgress) return;
        _exitInProgress = true;
        IsExitRequested = true;
        _log?.Append(LogLevel.Info, AppLogSources.App, "Выход из приложения.");
        try
        {
            if (_viewModel is not null)
                await _viewModel.StopForExitAsync();
        }
        catch (Exception ex)
        {
            _exceptions?.Report(ex, "Не удалось остановить VPN при выходе");
        }
        finally
        {
            _updateChecks?.Stop();
            _subscriptionRefresh?.Stop();
            _tray?.Dispose();
            _singleInstance?.Dispose();
            if (launchAfterRelease is not null)
                LaunchAfterRelease(launchAfterRelease);
            _window?.Close();
            Shutdown();
        }
    }

    private void LaunchAfterRelease(Action launch)
    {
        try
        {
            launch();
        }
        catch (Exception ex)
        {
            _exceptions?.Report(ex, "Не удалось запустить установщик обновления");
            AppDialogs.ShowUnexpectedError(
                _window,
                $"Не удалось запустить установщик обновления: {ex.Message}\nVPN отключён. Запустите Nothing VPN снова.");
        }
    }
}

using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using NothingVpn.Infrastructure.Composition;
using NothingVpn.Presentation;
using NothingVpn.Application.Services;
using System.Windows.Threading;
using NothingVpn.Infrastructure.TunApps;

namespace NothingVpn.Desktop.Wpf;

public partial class App : System.Windows.Application
{
    private const string AppId = "NothingVpn.Desktop.Wpf";
    private SingleInstance? _singleInstance;
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private SubscriptionViewModel? _subscriptionViewModel;
    private ISubscriptionService? _subscriptionService;
    private DispatcherTimer? _subscriptionTimer;
    private int _subscriptionRefreshRunning;
    private Forms.ToolStripMenuItem? _trayConnect;
    private Forms.ToolStripMenuItem? _trayDisconnect;
    private bool _exitInProgress;

    public static bool IsExitRequested { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ThemeManager.ApplySystemTheme();
        var smokeTest = e.Args.Any(x => string.Equals(x, "--smoke-test", StringComparison.OrdinalIgnoreCase));
        var smokeErrorPath = System.IO.Path.Combine(AppContext.BaseDirectory, "wpf-smoke-error.txt");
        if (smokeTest) { try { System.IO.File.Delete(smokeErrorPath); } catch { } }
        var takeover = e.Args.Any(x => string.Equals(x, "--takeover", StringComparison.OrdinalIgnoreCase));
        bool alreadyRunning;
        if (takeover)
        {
            _singleInstance = WaitForPrimaryTakeover();
            alreadyRunning = false;
        }
        else
        {
            _singleInstance = SingleInstance.TryCreatePrimary(AppId, out alreadyRunning);
        }
        if (alreadyRunning)
        {
            SingleInstance.ForwardToPrimary(AppId, e.Args);
            Shutdown();
            return;
        }
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        try
        {
            var services = ApplicationServicesFactory.CreateDefault();
            _subscriptionService = services.SubscriptionService;
            var connectionController = new ConnectionController(
                services.VpnConnectionService,
                services.AppLifecycleService);
            var screenController = new ConnectionScreenController(
                services.ProfileService,
                services.SettingsService);
            var profileController = new ProfileManagementController(
                services.ProfileService,
                services.SettingsService.GetState().ActiveProfileId);
            var subscriptionController = new SubscriptionManagementController(services.SubscriptionService);
            var subscriptionViewModel = new SubscriptionViewModel(subscriptionController);
            _subscriptionViewModel = subscriptionViewModel;
            var profileViewModel = new ProfileViewModel(profileController, subscriptionViewModel);
            var settingsViewModel = new SettingsViewModel(
                new ConnectionSettingsController(services.SettingsService),
                new TunAppsController(services.PathPolicy, services.SettingsService),
                new RuleSetManagementController(services.SettingsService),
                services.RuleSetFileService,
                new TunAppsSelectionService(new CompositeInstalledAppsProvider(new RegistryUninstallAppsProvider(),new AppPathsRegistryProvider(),new StartMenuShortcutAppsProvider()),new RunningProcessesProvider()),
                services.SettingsService,
                services.SettingsService.GetState(),
                new UpdateViewModel(controller: new AppUpdateController(services.AppUpdateService, services.SettingsService),
                    installer: services.InstallerUpdateService, launcher: services.InstallerLaunchService,
                    settings: services.SettingsService, state: services.SettingsService.GetState(), exit: RequestExit));

            _viewModel = new MainViewModel(screenController, connectionController, profileViewModel, settingsViewModel,
                new ConnectionDiagnosticController(services.DiagnosticsService), services.SharedLogStore, RequestExit);
            _viewModel.ConnectionStateChanged += (_, connected) => UpdateTrayState(connected);
            _window = new MainWindow(_viewModel);
            MainWindow = _window;
            CreateTrayIcon();
            UpdateTrayState(connectionController.IsRunning);
            _window.Show();
            if (smokeTest)
            {
                Dispatcher.BeginInvoke(ExitSmokeTest);
                return;
            }
            ShowStorageIssues(services.StorageHealthService);
            _ = settingsViewModel.Updates.CheckAtStartupAsync();
            _ = _viewModel.ApplyStartupAsync(StartupOptions.Parse(e.Args));

            _subscriptionTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            _subscriptionTimer.Tick += async (_, _) => await RefreshDueSubscriptionsAsync();
            _subscriptionTimer.Start();
            _ = RefreshSubscriptionsAfterStartupAsync();

            _singleInstance.StartServer(args => Dispatcher.Invoke(async () => { ShowMainWindow(); if(_viewModel is not null) await _viewModel.ApplyStartupAsync(StartupOptions.Parse(args)); }));
        }
        catch (Exception ex)
        {
            if (smokeTest)
            {
                Console.Error.WriteLine(ex);
                try { System.IO.File.WriteAllText(smokeErrorPath, ex.ToString()); } catch { }
                Shutdown(1);
                return;
            }
            System.Windows.MessageBox.Show(
                ex.Message,
                "Nothing VPN",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            RequestExit();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (Microsoft.Win32.UserPreferenceCategory.General
            or Microsoft.Win32.UserPreferenceCategory.Color
            or Microsoft.Win32.UserPreferenceCategory.VisualStyle
            or Microsoft.Win32.UserPreferenceCategory.Accessibility)) return;
        Dispatcher.BeginInvoke(ThemeManager.ApplySystemTheme);
    }

    private static SingleInstance? WaitForPrimaryTakeover()
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var instance = SingleInstance.TryCreatePrimary(AppId, out var alreadyRunning);
            if (!alreadyRunning && instance is not null)
                return instance;
            Thread.Sleep(200);
        }
        return null;
    }

    private void CreateTrayIcon()
    {
        var icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon ?? Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Nothing VPN"
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        _trayConnect = new Forms.ToolStripMenuItem("Подключить", null, async (_, _) => { if(_viewModel is not null) await _viewModel.ConnectFromTrayAsync(); });
        _trayDisconnect = new Forms.ToolStripMenuItem("Отключить", null, async (_, _) => { if(_viewModel is not null) await _viewModel.DisconnectFromTrayAsync(); });
        menu.Items.Add(_trayConnect); menu.Items.Add(_trayDisconnect);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(RequestExit));
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    private void UpdateTrayState(bool connected)
    {
        if (_trayIcon is not null)
            _trayIcon.Text = connected ? "Nothing VPN — подключено" : "Nothing VPN — отключено";
        if(_trayConnect is not null)_trayConnect.Enabled=!connected;
        if(_trayDisconnect is not null)_trayDisconnect.Enabled=connected;
    }

    private void ShowMainWindow() => _window?.BringToFront();

    private void ExitSmokeTest()
    {
        IsExitRequested = true;
        if (_trayIcon is not null) { _trayIcon.Visible=false; _trayIcon.ContextMenuStrip?.Dispose(); _trayIcon.Dispose(); }
        _singleInstance?.Dispose();
        _window?.Close();
        Shutdown(0);
    }

    private void ShowStorageIssues(IStorageHealthService health)
    {
        var issues=health.DrainIssues(); if(issues.Count==0)return;
        var recovered=issues.All(x=>x.RecoveredFromBackup);
        System.Windows.MessageBox.Show(_window,
            recovered?"Повреждённый файл данных восстановлен из резервной копии.":"Некоторые данные не удалось прочитать. Резервные файлы сохранены для восстановления.",
            "Nothing VPN",System.Windows.MessageBoxButton.OK,recovered?System.Windows.MessageBoxImage.Warning:System.Windows.MessageBoxImage.Error);
    }

    private async Task RefreshSubscriptionsAfterStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        await RefreshDueSubscriptionsAsync();
    }

    private async Task RefreshDueSubscriptionsAsync()
    {
        if (_subscriptionService is null || Interlocked.Exchange(ref _subscriptionRefreshRunning, 1) == 1)
            return;
        try
        {
            var results = await _subscriptionService.RefreshAllDueAsync();
            if (results.Count > 0)
                _subscriptionViewModel?.ReloadAfterExternalRefresh();
        }
        catch { }
        finally { Interlocked.Exchange(ref _subscriptionRefreshRunning, 0); }
    }

    private async void RequestExit()
    {
        if (_exitInProgress) return;
        _exitInProgress = true;
        IsExitRequested = true;
        try
        {
            if (_viewModel is not null)
                await _viewModel.StopForExitAsync();
        }
        catch { }
        finally
        {
            _subscriptionTimer?.Stop();
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.ContextMenuStrip?.Dispose();
                _trayIcon.Dispose();
            }
            _singleInstance?.Dispose();
            _window?.Close();
            Shutdown();
        }
    }
}

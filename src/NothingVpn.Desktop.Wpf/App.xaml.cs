using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using NothingVpn.Infrastructure.Composition;
using NothingVpn.Presentation;
using NothingVpn.Application.Services;
using System.Windows.Threading;

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
    private bool _exitInProgress;

    public static bool IsExitRequested { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
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
                services.SettingsService.GetState());

            _viewModel = new MainViewModel(screenController, connectionController, profileViewModel, settingsViewModel,
                new ConnectionDiagnosticController(services.DiagnosticsService), services.SharedLogStore, RequestExit);
            _viewModel.ConnectionStateChanged += (_, connected) => UpdateTrayState(connected);
            _window = new MainWindow(_viewModel);
            MainWindow = _window;
            CreateTrayIcon();
            UpdateTrayState(connectionController.IsRunning);
            _window.Show();

            _subscriptionTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            _subscriptionTimer.Tick += async (_, _) => await RefreshDueSubscriptionsAsync();
            _subscriptionTimer.Start();
            _ = RefreshSubscriptionsAfterStartupAsync();

            _singleInstance.StartServer(_ => Dispatcher.Invoke(ShowMainWindow));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "Nothing VPN",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            RequestExit();
        }
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
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(RequestExit));
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    private void UpdateTrayState(bool connected)
    {
        if (_trayIcon is not null)
            _trayIcon.Text = connected ? "Nothing VPN — подключено" : "Nothing VPN — отключено";
    }

    private void ShowMainWindow() => _window?.BringToFront();

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

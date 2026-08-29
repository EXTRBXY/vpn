using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using NothingVpn.Infrastructure.Composition;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

public partial class App : System.Windows.Application
{
    private const string AppId = "NothingVpn.Desktop.Wpf";
    private SingleInstance? _singleInstance;
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private bool _exitInProgress;

    public static bool IsExitRequested { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = SingleInstance.TryCreatePrimary(AppId, out var alreadyRunning);
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
            var connectionController = new ConnectionController(
                services.VpnConnectionService,
                services.AppLifecycleService);
            var screenController = new ConnectionScreenController(
                services.ProfileService,
                services.SettingsService);

            _viewModel = new MainViewModel(screenController, connectionController, RequestExit);
            _viewModel.ConnectionStateChanged += (_, connected) => UpdateTrayState(connected);
            _window = new MainWindow(_viewModel);
            MainWindow = _window;
            CreateTrayIcon();
            UpdateTrayState(connectionController.IsRunning);
            _window.Show();

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

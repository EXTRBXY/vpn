using System.Drawing;
using NothingVpn.Tray.Internal.Diagnostics;
using NothingVpn.Tray.Internal.SingBox;
using NothingVpn.Tray.Internal.Store;
using NothingVpn.Tray.Internal.WinInet;
using NothingVpn.Tray.Internal.Windows;

namespace NothingVpn.Tray;

internal sealed class MainAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly MainForm _mainForm;
    private readonly Icon _trayBaseIcon;
    private Icon? _trayStatusIcon;
    private readonly ToolStripMenuItem _trayItemConnect;
    private readonly ToolStripMenuItem _trayItemDisconnect;
    private bool _allowClose;
    private readonly SingleInstance _singleInstance;
    private readonly AppLogger _appLogger;

    public MainAppContext(StartupArgs? startup, SingleInstance singleInstance)
    {
        _singleInstance = singleInstance;

        var paths = AppPaths.CreateDefault();
        Directory.CreateDirectory(paths.BaseDir);
        Directory.CreateDirectory(paths.ConfigsDir);
        Directory.CreateDirectory(paths.RuleSetsDir);
        // No logs folder: logs are kept in-memory and exported on demand.

        var profileStore = new JsonProfileStore(paths.ProfilesJsonPath);
        var stateStore = new JsonStateStore(paths.StateJsonPath);
        AppState? stateSnapshot = null;
        try { stateSnapshot = stateStore.Load(); } catch { }
        var logStore = new InMemoryLogStore(maxBytes: 1_000_000);
        _appLogger = new AppLogger(logStore);
        var runner = new SingBoxRunner(
            paths,
            "sing-box.exe",
            logStore,
            debugLogs: () => (stateSnapshot ??= stateStore.Load()).DebugLogs,
            trustedSha256: () => (stateSnapshot ??= stateStore.Load()).TrustedSingBoxSha256);
        var proxy = new WinInetProxyController();

        var extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _trayBaseIcon = extracted is null ? (Icon)SystemIcons.Application.Clone() : (Icon)extracted.Clone();

        _mainForm = new MainForm(paths, profileStore, stateStore, runner, proxy, logStore, requestExit: Exit, vpnConnectionStateChanged: SetTrayConnectionState);
        _appLogger.Info("app/context", "MainAppContext инициализирован.");

        var menu = new ContextMenuStrip();
        _trayItemConnect = new ToolStripMenuItem("Подключить", null, (_, _) => _mainForm.ConnectFromTray());
        _trayItemDisconnect = new ToolStripMenuItem("Отключить", null, (_, _) => _mainForm.DisconnectFromTray());
        menu.Items.Add(_trayItemConnect);
        menu.Items.Add(_trayItemDisconnect);
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => Exit()));

        _tray = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = menu
        };
        SetTrayConnectionState(false);
        _tray.DoubleClick += (_, _) => ShowMain();

        _mainForm.Show();
        _mainForm.ApplyStartup(startup);

        _mainForm.Resize += (_, _) =>
        {
            if (_mainForm.WindowState == FormWindowState.Minimized)
            {
                HideMain();
            }
        };

        _mainForm.FormClosing += (_, e) =>
        {
            // Intercept user close (X) -> minimize to tray.
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideMain();
            }
        };

        Application.ApplicationExit += (_, _) => SafeShutdown();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeShutdown();

        // IPC for single-instance: bring to front and apply startup args from secondary invocations.
        _singleInstance.StartServer(argv =>
        {
            try
            {
                var parsed = StartupArgs.Parse(argv);
                _mainForm.BeginInvoke(() =>
                {
                    try
                    {
                        ShowMain();
                        _mainForm.ApplyStartup(parsed);
                    }
                    catch
                    {
                        _appLogger.Warn("app/context", "Не удалось применить аргументы вторичного инстанса.");
                    }
                });
            }
            catch
            {
                _appLogger.Warn("app/context", "Ошибка обработки IPC-запроса вторичного инстанса.");
            }
        });
    }

    private void ShowMain()
    {
        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private void HideMain()
    {
        _mainForm.Hide();
    }

    private void SetTrayConnectionState(bool connected)
    {
        void Apply()
        {
            _trayItemConnect.Enabled = !connected;
            _trayItemDisconnect.Enabled = connected;
            _trayStatusIcon?.Dispose();
            _trayStatusIcon = TrayStatusIconBuilder.Create(_trayBaseIcon, connected);
            _tray.Icon = _trayStatusIcon;
            _tray.Text = connected ? "Nothing VPN — подключено" : "Nothing VPN — отключено";
        }

        if (_mainForm.InvokeRequired)
            _mainForm.BeginInvoke(Apply);
        else
            Apply();
    }

    private void Exit()
    {
        _appLogger.Info("app/context", "Запрошен выход из приложения.");
        _allowClose = true;
        SafeShutdown();
        _tray.Visible = false;
        try { _tray.Icon = null; } catch { }
        _tray.Dispose();
        _trayStatusIcon?.Dispose();
        _trayStatusIcon = null;
        _trayBaseIcon.Dispose();
        _mainForm.Close();
        Application.Exit();
    }

    private void SafeShutdown()
    {
        try
        {
            if (_mainForm.InvokeRequired)
                _mainForm.Invoke(() => _mainForm.Shutdown());
            else
                _mainForm.Shutdown();
        }
        catch
        {
            _appLogger.Warn("app/context", "SafeShutdown завершился с ошибкой (best-effort).");
        }
    }
}


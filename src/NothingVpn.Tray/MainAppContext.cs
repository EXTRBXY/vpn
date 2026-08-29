using System.Drawing;
using NothingVpn.Application.Services;
using NothingVpn.Infrastructure.Composition;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Store;
using NothingVpn.Tray.Internal.Diagnostics;
using NothingVpn.Tray.Internal.Windows;
using NothingVpn.Tray.Internal.UI;
using NothingVpn.Presentation;

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
    private readonly ISubscriptionService _subscriptionService;
    private readonly System.Windows.Forms.Timer _subscriptionRefreshTimer;
    private int _subscriptionRefreshRunning;

    public MainAppContext(StartupArgs? startup, SingleInstance singleInstance)
    {
        _singleInstance = singleInstance;

        var paths = AppPaths.CreateDefault();
        Directory.CreateDirectory(paths.BaseDir);
        Directory.CreateDirectory(paths.ConfigsDir);
        Directory.CreateDirectory(paths.RuleSetsDir);

        var services = ApplicationServicesFactory.CreateDefault();
        var logStore = services.SharedLogStore;
        _appLogger = new AppLogger(logStore);
        _subscriptionService = services.SubscriptionService;
        var connectionController = new ConnectionController(
            services.VpnConnectionService,
            services.AppLifecycleService);
        var connectionScreenController = new ConnectionScreenController(
            services.ProfileService,
            services.SettingsService);
        var connectionSettingsController = new ConnectionSettingsController(services.SettingsService);
        var tunAppsController = new TunAppsController(services.PathPolicy, services.SettingsService);
        var ruleSetManagementController = new RuleSetManagementController(services.SettingsService);
        var connectionDiagnosticController = new ConnectionDiagnosticController(services.DiagnosticsService);

        var extracted = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
        _trayBaseIcon = extracted is null ? (Icon)SystemIcons.Application.Clone() : (Icon)extracted.Clone();

        _mainForm = new MainForm(
            paths,
            services.ProfileService,
            services.SubscriptionService,
            connectionScreenController,
            connectionSettingsController,
            tunAppsController,
            ruleSetManagementController,
            services.RuleSetFileService,
            services.AppUpdateService,
            connectionController,
            connectionDiagnosticController,
            logStore,
            requestExit: Exit,
            vpnConnectionStateChanged: SetTrayConnectionState);

        var menu = new ContextMenuStrip();
        menu.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        if (!UiTheme.IsHighContrast)
        {
            menu.BackColor = UiTheme.Surface;
            menu.ForeColor = UiTheme.TextPrimary;
        }
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
        ShowStorageIssues(services.StorageHealthService, paths.BaseDir);
        _mainForm.ApplyStartup(startup);

        _subscriptionRefreshTimer = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };
        _subscriptionRefreshTimer.Tick += (_, _) => _ = RefreshDueSubscriptionsQuietlyAsync();
        _subscriptionRefreshTimer.Start();
        _ = ScheduleStartupSubscriptionRefreshAsync();

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

        System.Windows.Forms.Application.ApplicationExit += (_, _) => SafeShutdown();
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

    private void ShowStorageIssues(IStorageHealthService storageHealthService, string dataDirectory)
    {
        var issues = storageHealthService.DrainIssues();
        if (issues.Count == 0)
            return;

        foreach (var issue in issues)
            _appLogger.Warn("app/storage", $"{issue.Message} Файл: {issue.Path}");

        var unrecovered = issues.Where(x => !x.RecoveredFromBackup).ToList();
        var message = unrecovered.Count == 0
            ? "Обнаружено повреждение файла данных. Приложение автоматически восстановило резервную копию."
            : "Не удалось прочитать один или несколько файлов данных и их резервные копии. " +
              "Повреждённые файлы не перезаписаны; для соответствующих разделов временно используются пустые данные.";

        message += $"\n\nКаталог данных:\n{dataDirectory}\n\nПодробности записаны в журнал приложения.";
        MessageBox.Show(
            _mainForm,
            message,
            "Проверка данных Nothing VPN",
            MessageBoxButtons.OK,
            unrecovered.Count == 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
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
        _subscriptionRefreshTimer?.Stop();
        _subscriptionRefreshTimer?.Dispose();
        SafeShutdown();
        _tray.Visible = false;
        try { _tray.Icon = null; } catch { }
        _tray.Dispose();
        _trayStatusIcon?.Dispose();
        _trayStatusIcon = null;
        _trayBaseIcon.Dispose();
        _mainForm.Close();
        System.Windows.Forms.Application.Exit();
    }

    private async Task ScheduleStartupSubscriptionRefreshAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            await RefreshDueSubscriptionsQuietlyAsync().ConfigureAwait(true);
        }
        catch
        {
            _appLogger.Warn("app/subscription", "Не удалось выполнить отложенное обновление подписок при старте.");
        }
    }

    private async Task RefreshDueSubscriptionsQuietlyAsync()
    {
        if (Interlocked.Exchange(ref _subscriptionRefreshRunning, 1) == 1)
            return;

        try
        {
            var results = await _subscriptionService.RefreshAllDueAsync().ConfigureAwait(true);
            if (results.Count == 0)
                return;

            var ok = results.Count(r => r.Success);
            var failed = results.Count - ok;
            _appLogger.Info("app/subscription", $"Автообновление подписок: успешно {ok}, ошибок {failed}.");
            if (failed > 0)
            {
                foreach (var r in results.Where(x => !x.Success))
                    _appLogger.Warn("app/subscription", $"Подписка {r.SubscriptionId}: {r.Error}");
            }

            _mainForm.BeginInvoke(_mainForm.ReloadProfilesFromSubscriptions);
        }
        catch (Exception ex)
        {
            _appLogger.Warn("app/subscription", $"Автообновление подписок: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _subscriptionRefreshRunning, 0);
        }
    }

    private void SafeShutdown()
    {
        try
        {
            _mainForm.Shutdown();
        }
        catch
        {
            _appLogger.Warn("app/context", "SafeShutdown завершился с ошибкой (best-effort).");
        }
    }
}


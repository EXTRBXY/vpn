using System.Windows.Threading;
using NothingVpn.Application.Models;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace NothingVpn.Desktop.Wpf;

/// <summary>Значок в трее: меню, состояние подключения и уведомление об обновлении.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<Exception> _reportError;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _connectItem;
    private readonly Forms.ToolStripMenuItem _disconnectItem;
    private Drawing.Icon? _baseIcon;
    private Drawing.Icon? _statusIcon;
    private bool _disposed;

    public TrayIcon(
        Dispatcher dispatcher,
        Action open,
        Func<Task> connect,
        Func<Task> disconnect,
        Action exit,
        Action<Exception> reportError)
    {
        _dispatcher = dispatcher;
        _reportError = reportError;
        _baseIcon = LoadApplicationIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _baseIcon,
            Visible = true,
            Text = "Nothing VPN"
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => InvokeOnDispatcher(open));
        // Асинхронные обработчики: ошибка уходит в диспетчер и попадает в глобальный обработчик.
        _connectItem = new Forms.ToolStripMenuItem("Подключить", null, async (_, _) => await connect());
        _disconnectItem = new Forms.ToolStripMenuItem("Отключить", null, async (_, _) => await disconnect());
        menu.Items.Add(_connectItem);
        menu.Items.Add(_disconnectItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => InvokeOnDispatcher(exit));
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => InvokeOnDispatcher(open);
        _notifyIcon.BalloonTipClicked += (_, _) => InvokeOnDispatcher(open);
    }

    public void SetConnected(bool connected)
    {
        if (_disposed) return;
        var nextIcon = TrayStatusIconBuilder.Create(_baseIcon ?? Drawing.SystemIcons.Application, connected);
        _notifyIcon.Icon = nextIcon;
        _statusIcon?.Dispose();
        _statusIcon = nextIcon;
        _notifyIcon.Text = connected ? "Nothing VPN — подключено" : "Nothing VPN — отключено";
        _connectItem.Enabled = !connected;
        _disconnectItem.Enabled = connected;
    }

    public void ShowUpdateAvailable(AppReleaseModel release)
    {
        if (_disposed) return;
        _notifyIcon.BalloonTipTitle = "Доступно обновление Nothing VPN";
        _notifyIcon.BalloonTipText = $"Версия {release.Semver} готова к загрузке. Нажмите, чтобы открыть приложение.";
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(8000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _statusIcon?.Dispose();
        _statusIcon = null;
        _baseIcon?.Dispose();
        _baseIcon = null;
    }

    // Исключение из Dispatcher.Invoke вернулось бы в цикл сообщений WinForms, минуя обработчик WPF.
    private void InvokeOnDispatcher(Action action)
    {
        try { _dispatcher.Invoke(action); }
        catch (Exception ex) { _reportError(ex); }
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        var extractedIcon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        var icon = extractedIcon is null
            ? (Drawing.Icon)Drawing.SystemIcons.Application.Clone()
            : (Drawing.Icon)extractedIcon.Clone();
        extractedIcon?.Dispose();
        return icon;
    }
}

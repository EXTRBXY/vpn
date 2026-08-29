using NothingVpn.Presentation;

namespace NothingVpn.Tray.Internal.UI;

internal sealed class ConnectionStatusPanelControl : UserControl
{
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _administrator = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _mode = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _profile = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _port = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _dns = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _ruleSets = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _tun = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _proxyBypass = new() { AutoSize = true, Anchor = AnchorStyles.Left };

    public ConnectionStatusPanelControl()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 9,
            Padding = new Padding(UiMetrics.Space12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "Статус", _status);
        AddRow(layout, 1, "Права", _administrator);
        AddRow(layout, 2, "Режим", _mode);
        AddRow(layout, 3, "Профиль", _profile);
        AddRow(layout, 4, "Порт", _port);
        AddRow(layout, 5, "DNS", _dns);
        AddRow(layout, 6, "Rule-sets", _ruleSets);
        AddRow(layout, 7, "TUN", _tun);
        AddRow(layout, 8, "Исключения прокси", _proxyBypass);
        Controls.Add(layout);
    }

    public void Apply(ConnectionViewState state)
    {
        _status.Text = state.StatusText;
        _administrator.Text = state.AdministratorText;
        _mode.Text = state.ModeText;
        _profile.Text = state.ProfileText;
        _port.Text = state.PortText;
        _dns.Text = state.DnsText;
        _ruleSets.Text = state.RuleSetsText;
        _tun.Text = state.TunText;
        _proxyBypass.Text = state.ProxyBypassText;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string caption, Control value)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(value, 1, row);
    }
}

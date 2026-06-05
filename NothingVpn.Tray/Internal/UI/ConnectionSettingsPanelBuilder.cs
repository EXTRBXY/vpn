using NothingVpn.Application.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Tray.Internal.UI;

internal sealed class ConnectionSettingsUi
{
    public required TabPage Tab { get; init; }
    public required Panel ProxySection { get; init; }
    public required TextBox ProxyOverrideBox { get; init; }
    public required Button ProxyOverrideResetBtn { get; init; }
    public required Panel TunSection { get; init; }
    public required Panel TunAppsHost { get; init; }
    public required TextBox TunInterfaceNameBox { get; init; }
    public required ComboBox TunAddressCidrModeCombo { get; init; }
    public required TextBox TunAddressCidrBox { get; init; }
    public required NumericUpDown TunMtu { get; init; }
    public required ComboBox TunStackCombo { get; init; }
    public required CheckBox TunAutoRoute { get; init; }
    public required CheckBox TunStrictRoute { get; init; }
    public required Label TunStrictRouteHint { get; init; }
    public required Panel DnsSection { get; init; }
    public required ComboBox DnsModeCombo { get; init; }
    public required ComboBox DnsPresetCombo { get; init; }
    public required ComboBox DnsDetourCombo { get; init; }
    public required TextBox DohServerBox { get; init; }
    public required TextBox DohPathBox { get; init; }
    public required TextBox DohSniBox { get; init; }
    public required Label DnsNotice { get; init; }
    public required TableLayoutPanel DohFieldsPanel { get; init; }

    public void UpdateVisibility(string? mode)
    {
        var normalized = ConnectionPolicy.NormalizeMode(mode);
        var isProxy = normalized == ConnectionPolicy.ProxyMode;
        var isTun = ConnectionPolicy.IsTunMode(normalized);

        ProxySection.Visible = isProxy;
        TunSection.Visible = isTun;
        TunStrictRouteHint.Visible = string.Equals(normalized, ConnectionPolicy.TunAppsMode, StringComparison.Ordinal);
    }

    public void UpdateDohFieldsEnabled()
    {
        var isDoh = DnsModeCombo.SelectedIndex == 1;
        DohFieldsPanel.Enabled = isDoh;
        DnsPresetCombo.Enabled = isDoh;
        DnsDetourCombo.Enabled = isDoh;
        DohServerBox.ReadOnly = !isDoh;
        DohPathBox.ReadOnly = !isDoh;
        DohSniBox.ReadOnly = !isDoh;
    }

    public void LoadFromState(AppStateModel state)
    {
        ProxyOverrideBox.Text = state.ProxyOverride ?? ProxyConnectionPolicy.DefaultProxyOverride;

        TunInterfaceNameBox.Text = state.TunInterfaceName ?? "NothingVpn";
        var cidr = (state.TunAddressCidr ?? "auto").Trim();
        if (cidr.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || cidr.Equals("172.19.0.1/30", StringComparison.OrdinalIgnoreCase)
            || cidr.Length == 0)
        {
            TunAddressCidrModeCombo.SelectedIndex = 0;
            TunAddressCidrBox.Text = "";
            TunAddressCidrBox.Enabled = false;
        }
        else
        {
            TunAddressCidrModeCombo.SelectedIndex = 1;
            TunAddressCidrBox.Text = cidr;
            TunAddressCidrBox.Enabled = true;
        }

        TunMtu.Value = TunSettingsPolicy.NormalizeMtu(state.TunMtu);
        TunStackCombo.SelectedIndex = TunSettingsPolicy.StackToComboIndex(state.TunStack);
        TunAutoRoute.Checked = state.TunAutoRoute;
        TunStrictRoute.Checked = state.TunStrictRoute;

        var dnsMode = (state.DnsMode ?? "doh").Trim().ToLowerInvariant();
        DnsModeCombo.SelectedIndex = dnsMode == "system" ? 0 : 1;
        DohServerBox.Text = state.DohServer ?? "";
        DohPathBox.Text = state.DohPath ?? "/dns-query";
        DohSniBox.Text = state.DohSni ?? "";
        DnsDetourCombo.SelectedIndex = DnsPolicy.DetourToComboIndex(state.DnsDetour);
        DnsPresetCombo.SelectedIndex = DnsPolicy.StateToPresetIndex(new Domain.Models.DnsSettings
        {
            DohServer = state.DohServer ?? "",
            DohSni = state.DohSni ?? ""
        });

        UpdateDohFieldsEnabled();
    }

    public void SetConnectionFieldsEnabled(bool enabled)
    {
        ProxyOverrideBox.ReadOnly = !enabled;
        ProxyOverrideResetBtn.Enabled = enabled;
        TunInterfaceNameBox.ReadOnly = !enabled;
        TunAddressCidrModeCombo.Enabled = enabled;
        if (enabled)
            TunAddressCidrBox.Enabled = TunAddressCidrModeCombo.SelectedIndex == 1;
        else
            TunAddressCidrBox.Enabled = false;
        TunMtu.Enabled = enabled;
        TunStackCombo.Enabled = enabled;
        TunAutoRoute.Enabled = enabled;
        TunStrictRoute.Enabled = enabled;
        DnsModeCombo.Enabled = enabled;
        if (enabled)
            UpdateDohFieldsEnabled();
        else
        {
            DnsPresetCombo.Enabled = false;
            DnsDetourCombo.Enabled = false;
            DohServerBox.ReadOnly = true;
            DohPathBox.ReadOnly = true;
            DohSniBox.ReadOnly = true;
        }
    }
}

internal static class ConnectionSettingsPanelBuilder
{
    public static ConnectionSettingsUi Build()
    {
        var tab = new TabPage("Соединение") { AutoScroll = true };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(UiMetrics.Space12)
        };

        var proxySection = BuildProxySection(out var proxyOverrideBox, out var proxyResetBtn);
        var tunSection = BuildTunSection(out var tunAppsHost, out var tunInterfaceName, out var tunCidrMode,
            out var tunCidrBox, out var tunMtu, out var tunStack, out var tunAutoRoute, out var tunStrictRoute,
            out var tunStrictHint);
        var dnsSection = BuildDnsSection(out var dnsMode, out var dnsPreset, out var dnsDetour, out var dohServer,
            out var dohPath, out var dohSni, out var dnsNotice, out var dohFieldsPanel);

        root.Controls.Add(proxySection);
        root.Controls.Add(tunSection);
        root.Controls.Add(dnsSection);
        tab.Controls.Add(root);

        var ui = new ConnectionSettingsUi
        {
            Tab = tab,
            ProxySection = proxySection,
            ProxyOverrideBox = proxyOverrideBox,
            ProxyOverrideResetBtn = proxyResetBtn,
            TunSection = tunSection,
            TunAppsHost = tunAppsHost,
            TunInterfaceNameBox = tunInterfaceName,
            TunAddressCidrModeCombo = tunCidrMode,
            TunAddressCidrBox = tunCidrBox,
            TunMtu = tunMtu,
            TunStackCombo = tunStack,
            TunAutoRoute = tunAutoRoute,
            TunStrictRoute = tunStrictRoute,
            TunStrictRouteHint = tunStrictHint,
            DnsSection = dnsSection,
            DnsModeCombo = dnsMode,
            DnsPresetCombo = dnsPreset,
            DnsDetourCombo = dnsDetour,
            DohServerBox = dohServer,
            DohPathBox = dohPath,
            DohSniBox = dohSni,
            DnsNotice = dnsNotice,
            DohFieldsPanel = dohFieldsPanel
        };

        proxyResetBtn.Click += (_, _) =>
            proxyOverrideBox.Text = ProxyConnectionPolicy.DefaultProxyOverride;

        tunCidrMode.SelectedIndexChanged += (_, _) =>
        {
            var custom = tunCidrMode.SelectedIndex == 1;
            tunCidrBox.Enabled = custom;
            if (!custom)
                tunCidrBox.Text = "";
        };

        dnsMode.SelectedIndexChanged += (_, _) => ui.UpdateDohFieldsEnabled();

        return ui;
    }

    private static Panel BuildProxySection(out TextBox proxyOverrideBox, out Button resetBtn)
    {
        var section = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, UiMetrics.Space12)
        };

        var group = new GroupBox
        {
            Text = "Исключения прокси",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(UiMetrics.Space12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3
        };

        layout.Controls.Add(new Label
        {
            Text = "Адреса и маски, которые не идут через прокси (разделитель «;»).",
            AutoSize = true,
            MaximumSize = new Size(560, 0)
        }, 0, 0);

        proxyOverrideBox = new TextBox
        {
            Multiline = true,
            Height = 56,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Text = ProxyConnectionPolicy.DefaultProxyOverride
        };
        layout.Controls.Add(proxyOverrideBox, 0, 1);

        resetBtn = new Button { Text = "Сбросить по умолчанию", AutoSize = true };
        layout.Controls.Add(resetBtn, 0, 2);
        group.Controls.Add(layout);
        section.Controls.Add(group);
        return section;
    }

    private static Panel BuildTunSection(
        out Panel tunAppsHost,
        out TextBox interfaceName,
        out ComboBox cidrMode,
        out TextBox cidrBox,
        out NumericUpDown mtu,
        out ComboBox stack,
        out CheckBox autoRoute,
        out CheckBox strictRoute,
        out Label strictHint)
    {
        var section = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, UiMetrics.Space12),
            Visible = false
        };

        var group = new GroupBox
        {
            Text = "TUN",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(UiMetrics.Space12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        tunAppsHost = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        layout.SetColumnSpan(tunAppsHost, 2);
        layout.Controls.Add(tunAppsHost, 0, 0);

        interfaceName = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        layout.Controls.Add(new Label { Text = "Интерфейс", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(interfaceName, 1, 1);

        cidrMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        cidrMode.Items.AddRange(new object[] { "Авто", "Пользовательский CIDR" });
        cidrBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Enabled = false };
        var cidrRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2
        };
        cidrRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        cidrRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cidrRow.Controls.Add(cidrMode, 0, 0);
        cidrRow.Controls.Add(cidrBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Адрес TUN", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        var cidrColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2
        };
        cidrColumn.Controls.Add(cidrRow, 0, 0);
        cidrColumn.Controls.Add(new Label
        {
            Text = "В режиме «Авто» адрес назначается для каждого профиля отдельно.",
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 4, 0, 0)
        }, 0, 1);
        layout.Controls.Add(cidrColumn, 1, 2);

        mtu = new NumericUpDown
        {
            Minimum = TunSettingsPolicy.MinMtu,
            Maximum = TunSettingsPolicy.MaxMtu,
            Value = TunSettingsPolicy.DefaultMtu,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(new Label { Text = "MTU", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(mtu, 1, 3);

        stack = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        stack.Items.AddRange(new object[]
        {
            "Системный (рекомендуется)",
            "Смешанный",
            "gVisor"
        });
        layout.Controls.Add(new Label { Text = "Сетевой стек", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(stack, 1, 4);

        autoRoute = new CheckBox
        {
            Text = "Автоматически настраивать маршруты",
            AutoSize = true,
            Checked = true
        };
        layout.SetColumnSpan(autoRoute, 2);
        layout.Controls.Add(autoRoute, 0, 5);

        strictRoute = new CheckBox
        {
            Text = "Строгая маршрутизация (меньше утечек DNS)",
            AutoSize = true,
            Checked = true
        };
        layout.SetColumnSpan(strictRoute, 2);
        layout.Controls.Add(strictRoute, 0, 6);

        strictHint = new Label
        {
            Text = "В режиме TUN (выбранные приложения) строгая маршрутизация не применяется.",
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            MaximumSize = new Size(560, 0),
            Visible = false
        };
        layout.SetColumnSpan(strictHint, 2);
        layout.Controls.Add(strictHint, 0, 7);

        group.Controls.Add(layout);
        section.Controls.Add(group);
        return section;
    }

    private static Panel BuildDnsSection(
        out ComboBox dnsMode,
        out ComboBox dnsPreset,
        out ComboBox dnsDetour,
        out TextBox dohServer,
        out TextBox dohPath,
        out TextBox dohSni,
        out Label dnsNotice,
        out TableLayoutPanel dohFieldsPanel)
    {
        var section = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var group = new GroupBox
        {
            Text = "DNS",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(UiMetrics.Space12)
        };

        dnsMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        dnsMode.Items.AddRange(new object[] { "Системный DNS", "DoH (HTTPS)" });
        dnsMode.SelectedIndex = 1;

        dnsPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        dnsPreset.Items.AddRange(new object[]
        {
            "Cloudflare (1.1.1.1, SNI cloudflare-dns.com)",
            "Google (8.8.8.8, SNI dns.google)",
            "Quad9 (9.9.9.9, SNI dns.quad9.net)",
            "AdGuard (94.140.14.14, SNI dns.adguard.com)",
            "Пользовательский"
        });

        dnsDetour = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        dnsDetour.Items.AddRange(new object[] { "Напрямую", "Через VPN" });

        dohServer = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        dohPath = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        dohSni = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

        dohFieldsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 5
        };
        dohFieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        dohFieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Режим DNS", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(dnsMode, 1, 0);

        dohFieldsPanel.Controls.Add(new Label { Text = "Пресет DoH", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        dohFieldsPanel.Controls.Add(dnsPreset, 1, 0);

        dohFieldsPanel.Controls.Add(new Label { Text = "Маршрут DoH", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        dohFieldsPanel.Controls.Add(dnsDetour, 1, 1);
        dohFieldsPanel.Controls.Add(new Label { Text = "Сервер DoH", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        dohFieldsPanel.Controls.Add(dohServer, 1, 2);
        dohFieldsPanel.Controls.Add(new Label { Text = "Путь DoH", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        dohFieldsPanel.Controls.Add(dohPath, 1, 3);
        dohFieldsPanel.Controls.Add(new Label { Text = "SNI для TLS", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        dohFieldsPanel.Controls.Add(dohSni, 1, 4);

        layout.SetColumnSpan(dohFieldsPanel, 2);
        layout.Controls.Add(dohFieldsPanel, 0, 1);

        dnsNotice = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = UiTheme.TextMuted,
            Dock = DockStyle.Top,
            Padding = new Padding(2, 6, 2, 0),
            Visible = false
        };

        group.Controls.Add(layout);
        group.Controls.Add(dnsNotice);
        section.Controls.Add(group);
        return section;
    }
}

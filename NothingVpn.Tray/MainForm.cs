using System.ComponentModel;
using System.Diagnostics;
using NothingVpn.Application.Mappers;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Tray.Internal.Profile;
using NothingVpn.Tray.Internal.SingBox;
using NothingVpn.Tray.Internal.Security;
using NothingVpn.Tray.Internal.Store;
using NothingVpn.Tray.Internal.WinInet;
using NothingVpn.Tray.Internal.Diagnostics;
using NothingVpn.Tray.Internal.TunApps;
using NothingVpn.Tray.Internal.Windows;
using NothingVpn.Tray.Internal.RuleSets;
using NothingVpn.Tray.Internal.Updates;
using NothingVpn.Tray.Internal.UI;

namespace NothingVpn.Tray;

internal sealed class MainForm : Form
{
    private readonly AppPaths _paths;
    private readonly IProfileService _profileService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ISettingsService _settingsService;
    private readonly IVpnConnectionService _vpnConnectionService;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IAppLifecycleService _appLifecycleService;
    private readonly InMemoryLogStore _logStore;
    private readonly AppLogger _appLogger;
    private readonly System.Action _requestExit;
    private readonly System.Action<bool>? _vpnConnectionStateChanged;
    private readonly TunAppsSelectionService _tunAppsSelectionService;

    private AppStateModel _state = new();
    private IReadOnlyList<VpnProfile> _profiles = Array.Empty<VpnProfile>();

    private readonly TabControl _tabs;
    private readonly TabPage _tabLogs;
    private readonly ConnectionSettingsUi _connectionSettings;

    private readonly ComboBox _profilesCombo;
    private readonly Button _profilesBtn;
    private readonly Button _startBtn;
    private readonly Button _stopBtn;
    private readonly NumericUpDown _port;
    private readonly Button _pingBtn;
    private readonly TextBox _logBox;
    private readonly ComboBox _logFilterCombo;
    private readonly Button _copyLogsBtn;
    private readonly Button _downloadLogsBtn;
    private readonly System.Windows.Forms.Timer _logTimer;
    private readonly CheckBox _debugLogs;
    private readonly ComboBox _modeCombo;
    private readonly Button _trustSingBoxBtn;
    private readonly Label _singBoxHashLabel;
    private readonly Label _statusValue;
    private readonly Label _adminValue;
    private readonly Label _modeValue;
    private readonly Label _profileValue;
    private readonly Label _portValue;
    private readonly Label _dnsValue;
    private readonly Label _ruleSetsValue;
    private readonly Label _tunValue;
    private readonly Label _proxyBypassValue;

    private readonly Panel _tunAppsPanel;
    private readonly GroupBox _tunAppsGroup;
    private readonly ListView _tunAppsList;
    private readonly ImageList _tunAppIcons;
    private readonly TunAppIconCache _tunAppIconCache = new();
    private readonly Button _tunAppsAddBtn;
    private readonly Button _tunAppsBrowseFileBtn;
    private readonly Button _tunAppsRemoveBtn;

    private readonly DataGridView _builtinRuleSetsGrid;
    private readonly DataGridView _userRuleSetsGrid;
    private readonly Button _builtinRuleSetsFetchOrRemoveBtn;
    private readonly Button _builtinRuleSetsCheckUpdatesBtn;
    private readonly Button _userRuleSetsAddBtn;
    private readonly Button _userRuleSetsRemoveBtn;
    private readonly Button _builtinRuleSetsOtherListsBtn;
    private BindingList<UserRuleSetModel> _builtinRuleSetsBinding = new();
    private BindingList<UserRuleSetModel> _userRuleSetsBinding = new();

    private readonly ComboBox _dnsModeCombo;
    private readonly ComboBox _dnsPresetCombo;
    private readonly ComboBox _dnsDetourCombo;
    private readonly TextBox _dohServerBox;
    private readonly TextBox _dohPathBox;
    private readonly TextBox _dohSniBox;
    private readonly Label _dnsNotice;
    private readonly TextBox _proxyOverrideBox;
    private readonly TextBox _tunInterfaceNameBox;
    private readonly ComboBox _tunAddressCidrModeCombo;
    private readonly TextBox _tunAddressCidrBox;
    private readonly NumericUpDown _tunMtu;
    private readonly ComboBox _tunStackCombo;
    private readonly CheckBox _tunAutoRoute;
    private readonly CheckBox _tunStrictRoute;
    private readonly System.Windows.Forms.Timer _dnsDebounceTimer;
    private bool _dnsUiReady;

    private readonly Panel _updateBannerPanel;
    private readonly Label _updateBannerLabel;
    private readonly Button _updateBannerInstallCachedBtn;
    private readonly Button _updateBannerDownloadBtn;
    private readonly Button _updateManualCheckBtn;
    private readonly System.Windows.Forms.Timer _updatePeriodicTimer;
    private GitHubReleaseInfo? _updatePendingRelease;
    private bool _updateDownloadBusy;

    private int _lastLogVersion = -1;
    private int _lastLogMinLevel = -1;
    private bool _connectionUiUpdateQueued;

    public MainForm(
        AppPaths paths,
        IProfileService profileService,
        ISubscriptionService subscriptionService,
        ISettingsService settingsService,
        IVpnConnectionService vpnConnectionService,
        IDiagnosticsService diagnosticsService,
        IAppLifecycleService appLifecycleService,
        InMemoryLogStore logStore,
        System.Action? requestExit = null,
        System.Action<bool>? vpnConnectionStateChanged = null)
    {
        _paths = paths;
        _profileService = profileService;
        _subscriptionService = subscriptionService;
        _settingsService = settingsService;
        _vpnConnectionService = vpnConnectionService;
        _diagnosticsService = diagnosticsService;
        _appLifecycleService = appLifecycleService;
        _logStore = logStore;
        _appLogger = new AppLogger(logStore);
        _requestExit = requestExit ?? (() => System.Windows.Forms.Application.Exit());
        _vpnConnectionStateChanged = vpnConnectionStateChanged;
        _tunAppsSelectionService = new TunAppsSelectionService(
            new CompositeInstalledAppsProvider(
                new RegistryUninstallAppsProvider(),
                new AppPathsRegistryProvider(),
                new StartMenuShortcutAppsProvider()),
            new RunningProcessesProvider());

        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? SystemIcons.Application;
        Text = "Nothing VPN (прокси)";
        Width = 640;
        Height = 600;
        MinimumSize = new Size(520, 520);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        SuspendLayout();

        _tabs = new TabControl { Dock = DockStyle.Fill };
        var tabMain = new TabPage("Основное");
        _connectionSettings = ConnectionSettingsPanelBuilder.Build();
        var tabRouting = new TabPage("Маршрутизация");
        _tabLogs = new TabPage("Логи");
        var tabAdvanced = new TabPage("Дополнительно");
        _tabs.TabPages.Add(tabMain);
        _tabs.TabPages.Add(_connectionSettings.Tab);
        _tabs.TabPages.Add(tabRouting);
        _tabs.TabPages.Add(_tabLogs);
        _tabs.TabPages.Add(tabAdvanced);
        Controls.Add(_tabs);

        _dnsModeCombo = _connectionSettings.DnsModeCombo;
        _dnsPresetCombo = _connectionSettings.DnsPresetCombo;
        _dnsDetourCombo = _connectionSettings.DnsDetourCombo;
        _dohServerBox = _connectionSettings.DohServerBox;
        _dohPathBox = _connectionSettings.DohPathBox;
        _dohSniBox = _connectionSettings.DohSniBox;
        _dnsNotice = _connectionSettings.DnsNotice;
        _proxyOverrideBox = _connectionSettings.ProxyOverrideBox;
        _tunInterfaceNameBox = _connectionSettings.TunInterfaceNameBox;
        _tunAddressCidrModeCombo = _connectionSettings.TunAddressCidrModeCombo;
        _tunAddressCidrBox = _connectionSettings.TunAddressCidrBox;
        _tunMtu = _connectionSettings.TunMtu;
        _tunStackCombo = _connectionSettings.TunStackCombo;
        _tunAutoRoute = _connectionSettings.TunAutoRoute;
        _tunStrictRoute = _connectionSettings.TunStrictRoute;

        tabMain.AutoScroll = true;
        tabRouting.AutoScroll = true;
        tabAdvanced.AutoScroll = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(UiMetrics.Space12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "Профиль", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _profilesCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            MinimumSize = new Size(UiMetrics.MinInputWidth, 0)
        };
        layout.Controls.Add(_profilesCombo, 1, 0);
        _profilesBtn = new Button { Text = "Профили", AutoSize = true, Margin = new Padding(0, 2, 4, 2) };
        _pingBtn = new Button { Text = "Пинг", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        var profileActions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(6, 0, 0, 0),
            Padding = Padding.Empty
        };
        profileActions.Controls.Add(_profilesBtn);
        profileActions.Controls.Add(_pingBtn);
        layout.Controls.Add(profileActions, 2, 0);

        layout.Controls.Add(new Label { Text = "Режим", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _modeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            MinimumSize = new Size(UiMetrics.MinInputWidth, 0)
        };
        _modeCombo.Items.AddRange(new object[] { "Прокси", "TUN (весь трафик)", "TUN (выбранные приложения)" });
        layout.Controls.Add(_modeCombo, 1, 1);
        _startBtn = new Button { Text = "Старт", AutoSize = true, Margin = new Padding(0, 2, 4, 2) };
        _stopBtn = new Button { Text = "Стоп", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        var modeActions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(6, 0, 0, 0),
            Padding = Padding.Empty
        };
        modeActions.Controls.Add(_startBtn);
        modeActions.Controls.Add(_stopBtn);
        layout.Controls.Add(modeActions, 2, 1);

        layout.Controls.Add(new Label { Text = "Локальный порт", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        layout.Controls.Add(_port, 1, 2);
        layout.Controls.Add(new Label { Text = "", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(new Label { Text = "", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 3);
        // (Trust moved to Advanced tab)
        _trustSingBoxBtn = new Button { Text = "Доверять sing-box.exe", Anchor = AnchorStyles.Left };
        _singBoxHashLabel = new Label { Text = "", AutoSize = true, Anchor = AnchorStyles.Left };

        var mainStack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiMetrics.Space12, UiMetrics.Space12, UiMetrics.Space12, UiMetrics.Space8)
        };
        mainStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var connectionGroup = new GroupBox
        {
            Text = "Подключение",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, UiMetrics.Space8)
        };
        connectionGroup.Controls.Add(layout);

        _tunAppsPanel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(UiMetrics.Space12, 0, UiMetrics.Space12, UiMetrics.Space12),
            Visible = false
        };
        _tunAppsGroup = new GroupBox
        {
            Text = "TUN приложения",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, UiMetrics.Space8),
            Visible = false
        };
        var tunAppsRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3
        };
        tunAppsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tunAppsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        tunAppsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tunAppsRoot.Controls.Add(new Label
        {
            Text = "Исполняемые файлы, чей трафик идёт через VPN (полный путь .exe). Дочерние процессы нужно добавлять отдельно.\n\nСписки доменов из вкладки «Маршрутизация» применяются раньше выбора приложений.",
            AutoSize = true,
            MaximumSize = new Size(560, 0)
        }, 0, 0);
        _tunAppIcons = TunAppIconCache.CreateImageList();
        _tunAppsList = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            SmallImageList = _tunAppIcons
        };
        _tunAppsList.Columns.Add("Приложение", 160);
        _tunAppsList.Columns.Add("Путь", 360);
        tunAppsRoot.Controls.Add(_tunAppsList, 0, 1);
        var tunAppsBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _tunAppsAddBtn = new Button { Text = "Добавить", AutoSize = true };
        _tunAppsBrowseFileBtn = new Button { Text = "Указать в проводнике", AutoSize = true };
        _tunAppsRemoveBtn = new Button { Text = "Удалить", AutoSize = true };
        tunAppsBtns.Controls.Add(_tunAppsAddBtn);
        tunAppsBtns.Controls.Add(_tunAppsBrowseFileBtn);
        tunAppsBtns.Controls.Add(_tunAppsRemoveBtn);
        tunAppsRoot.Controls.Add(tunAppsBtns, 0, 2);
        _tunAppsPanel.Controls.Add(tunAppsRoot);
        _tunAppsGroup.Controls.Add(_tunAppsPanel);

        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 9,
            Padding = new Padding(UiMetrics.Space12),
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _statusValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _adminValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _modeValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _profileValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _portValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _dnsValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _ruleSetsValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _tunValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _proxyBypassValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };

        statusLayout.Controls.Add(new Label { Text = "Статус", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        statusLayout.Controls.Add(_statusValue, 1, 0);
        statusLayout.Controls.Add(new Label { Text = "Права", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        statusLayout.Controls.Add(_adminValue, 1, 1);
        statusLayout.Controls.Add(new Label { Text = "Режим", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        statusLayout.Controls.Add(_modeValue, 1, 2);
        statusLayout.Controls.Add(new Label { Text = "Профиль", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        statusLayout.Controls.Add(_profileValue, 1, 3);
        statusLayout.Controls.Add(new Label { Text = "Порт", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        statusLayout.Controls.Add(_portValue, 1, 4);
        statusLayout.Controls.Add(new Label { Text = "DNS", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        statusLayout.Controls.Add(_dnsValue, 1, 5);
        statusLayout.Controls.Add(new Label { Text = "Rule-sets", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 6);
        statusLayout.Controls.Add(_ruleSetsValue, 1, 6);
        statusLayout.Controls.Add(new Label { Text = "TUN", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 7);
        statusLayout.Controls.Add(_tunValue, 1, 7);
        statusLayout.Controls.Add(new Label { Text = "Исключения прокси", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 8);
        statusLayout.Controls.Add(_proxyBypassValue, 1, 8);
        for (var i = 0; i < 9; i++)
            statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var statusGroup = new GroupBox
        {
            Text = "Текущее состояние",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        statusGroup.Controls.Add(statusLayout);
        mainStack.Controls.Add(statusGroup, 0, 0);
        mainStack.Controls.Add(connectionGroup, 0, 1);
        tabMain.Controls.Add(mainStack);

        _connectionSettings.TunAppsHost.Controls.Add(_tunAppsGroup);
        _tunAppsGroup.Dock = DockStyle.Top;

        // Logs tab
        var logsRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        logsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _tabLogs.Controls.Add(logsRoot);

        var logsTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(UiMetrics.Space8),
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        logsRoot.Controls.Add(logsTop, 0, 0);

        logsTop.Controls.Add(new Label { Text = "Уровень", AutoSize = true, Margin = new Padding(6, 8, 6, 0) });
        _logFilterCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _logFilterCombo.Items.AddRange(new object[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR" });
        logsTop.Controls.Add(_logFilterCombo);

        _debugLogs = new CheckBox { Text = "Debug", AutoSize = true, Margin = new Padding(12, 6, 6, 0) };
        logsTop.Controls.Add(_debugLogs);

        _copyLogsBtn = new Button { Text = "Копировать", AutoSize = true, Margin = new Padding(12, 4, 6, 0) };
        logsTop.Controls.Add(_copyLogsBtn);
        _downloadLogsBtn = new Button { Text = "Скачать…", AutoSize = true, Margin = new Padding(6, 4, 6, 0) };
        logsTop.Controls.Add(_downloadLogsBtn);

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9f),
        };
        logsRoot.Controls.Add(_logBox, 0, 1);

        // Advanced tab
        var advLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(UiMetrics.Space12),
        };
        advLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        advLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        advLayout.Controls.Add(new Label { Text = "Проверка sing-box.exe", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        advLayout.Controls.Add(new Label
        {
            Text = "Если задан «доверенный» SHA-256, приложение проверяет, что запускается именно нужный sing-box.exe.",
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            Anchor = AnchorStyles.Left
        }, 1, 0);
        advLayout.Controls.Add(_trustSingBoxBtn, 0, 1);
        advLayout.Controls.Add(_singBoxHashLabel, 1, 1);

        var rsOuter = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0)
        };

        var builtinGroup = new GroupBox
        {
            Text = "Встроенные списки (sing-geosite)",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(UiMetrics.Space12),
            Margin = new Padding(0, 0, 0, UiMetrics.Space8)
        };
        var builtinLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2
        };
        builtinLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        builtinLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _builtinRuleSetsGrid = CreateRuleSetsDataGridView(multiSelect: true);
        builtinLayout.Controls.Add(_builtinRuleSetsGrid, 0, 0);

        var builtinBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _builtinRuleSetsFetchOrRemoveBtn = new Button { Text = "Скачать", AutoSize = true, Enabled = false };
        _builtinRuleSetsCheckUpdatesBtn = new Button { Text = "Проверить обновления…", AutoSize = true };
        _builtinRuleSetsOtherListsBtn = new Button { Text = "Другие списки", AutoSize = true };
        builtinBtns.Controls.Add(_builtinRuleSetsFetchOrRemoveBtn);
        builtinBtns.Controls.Add(_builtinRuleSetsCheckUpdatesBtn);
        builtinBtns.Controls.Add(_builtinRuleSetsOtherListsBtn);
        builtinLayout.Controls.Add(builtinBtns, 0, 1);
        builtinGroup.Controls.Add(builtinLayout);

        var userGroup = new GroupBox
        {
            Text = "Пользовательские списки (.srs)",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(UiMetrics.Space12)
        };
        var userLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2
        };
        userLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        userLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _userRuleSetsGrid = CreateRuleSetsDataGridView(multiSelect: false);
        userLayout.Controls.Add(_userRuleSetsGrid, 0, 0);

        var userBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _userRuleSetsAddBtn = new Button { Text = "Добавить .srs…", AutoSize = true };
        _userRuleSetsRemoveBtn = new Button { Text = "Удалить", AutoSize = true };
        userBtns.Controls.Add(_userRuleSetsAddBtn);
        userBtns.Controls.Add(_userRuleSetsRemoveBtn);
        userLayout.Controls.Add(userBtns, 0, 1);
        userGroup.Controls.Add(userLayout);

        rsOuter.Controls.Add(userGroup);
        rsOuter.Controls.Add(builtinGroup);
        tabRouting.Controls.Add(rsOuter);

        _updateBannerPanel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 10),
            Visible = false,
            BackColor = UiTheme.IsHighContrast ? SystemColors.Info : Color.FromArgb(234, 244, 255)
        };
        var updateBannerFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        _updateBannerLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 16, 0),
            MaximumSize = new Size(420, 0)
        };
        _updateBannerInstallCachedBtn = new Button
        {
            Text = AppUpdateUserMessages.ButtonInstallReady,
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 0),
            Visible = false
        };
        _updateBannerDownloadBtn = new Button
        {
            Text = AppUpdateUserMessages.ButtonDownloadInstall,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        updateBannerFlow.Controls.Add(_updateBannerLabel);
        updateBannerFlow.Controls.Add(_updateBannerInstallCachedBtn);
        updateBannerFlow.Controls.Add(_updateBannerDownloadBtn);
        _updateBannerPanel.Controls.Add(updateBannerFlow);

        var updateManualCheckPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(12, 8, 12, 8)
        };
        _updateManualCheckBtn = new Button
        {
            Text = AppUpdateUserMessages.ButtonCheckUpdates,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        _updateManualCheckBtn.Click += async (_, _) => await OnManualCheckForUpdatesClickAsync();
        updateManualCheckPanel.Controls.Add(_updateManualCheckBtn);

        tabAdvanced.Controls.Add(updateManualCheckPanel);
        tabAdvanced.Controls.Add(advLayout);
        tabAdvanced.Controls.Add(_updateBannerPanel);

        _updateBannerInstallCachedBtn.Click += (_, _) => OnUpdateBannerInstallCachedClick();
        _updateBannerDownloadBtn.Click += async (_, _) => await OnUpdateBannerDownloadClickAsync();

        _logTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _logTimer.Tick += (_, _) => RefreshLog();

        _dnsDebounceTimer = new System.Windows.Forms.Timer { Interval = 350 };
        _dnsDebounceTimer.Tick += (_, _) =>
        {
            _dnsDebounceTimer.Stop();
            if (!_dnsUiReady) return;
            SaveConnectionSettingsFromUi(showDialogs: false);
        };

        _profilesBtn.Click += (_, _) => OpenProfilesDialog();
        _startBtn.Click += async (_, _) => await StartAsync();
        _stopBtn.Click += (_, _) => _ = StopAsync();
        _profilesCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_profilesCombo.SelectedItem is VpnProfile p)
            {
                _state.ActiveProfileId = p.Id;
                SaveState();
                UpdateButtons();
            }
        };
        _port.ValueChanged += (_, _) =>
        {
            _state.LocalMixedPort = (int)_port.Value;
            SaveState();
        };
        _modeCombo.SelectedIndexChanged += (_, _) =>
        {
            _state.Mode = ComboIndexToMode(_modeCombo.SelectedIndex);
            SaveState();
            UpdateTitle();
            UpdateConnectionTabVisibility();
            UpdateButtons();
        };
        _tunAppsAddBtn.Click += (_, _) => AddTunAppExecutable();
        _tunAppsBrowseFileBtn.Click += (_, _) => AddTunAppFromOpenFileDialog();
        _tunAppsRemoveBtn.Click += (_, _) => RemoveSelectedTunApp();
        _debugLogs.CheckedChanged += (_, _) =>
        {
            _state.DebugLogs = _debugLogs.Checked;
            // Keep sing-box logging minimal by default.
            _state.SingBoxLogLevel = _state.DebugLogs ? "debug" : "warn";
            SaveState();
        };
        _trustSingBoxBtn.Click += (_, _) => TrustCurrentSingBox();
        _copyLogsBtn.Click += (_, _) => CopyLogsToClipboard();
        _downloadLogsBtn.Click += (_, _) => DownloadLogs();
        _pingBtn.Click += async (_, _) => await PingAsync();
        _logFilterCombo.SelectedIndexChanged += (_, _) => RefreshLog();
        _userRuleSetsAddBtn.Click += (_, _) => AddRuleSet();
        _userRuleSetsRemoveBtn.Click += (_, _) => RemoveSelectedUserRuleSet();
        _builtinRuleSetsFetchOrRemoveBtn.Click += async (_, _) => await OnBuiltinFetchOrRemoveClickAsync();
        _builtinRuleSetsCheckUpdatesBtn.Click += async (_, _) => await CheckBuiltinRuleSetUpdatesAsync();
        _builtinRuleSetsOtherListsBtn.Click += (_, _) => OpenOtherRuleSetLists();
        _builtinRuleSetsGrid.CellBeginEdit += BuiltinRuleSetsGrid_CellBeginEdit;
        _builtinRuleSetsGrid.SelectionChanged += (_, _) => UpdateBuiltinFetchOrRemoveButton();
        _builtinRuleSetsGrid.DataBindingComplete += (_, _) =>
        {
            RefreshBuiltinGridRowStyles();
            UpdateBuiltinFetchOrRemoveButton();
        };
        _builtinRuleSetsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_builtinRuleSetsGrid.IsCurrentCellDirty)
                _builtinRuleSetsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _builtinRuleSetsGrid.CellValueChanged += (_, _) =>
        {
            SaveRuleSetsFromGrid();
            RefreshBuiltinGridRowStyles();
        };
        _builtinRuleSetsGrid.DataError += (_, _) => { };

        _userRuleSetsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_userRuleSetsGrid.IsCurrentCellDirty)
                _userRuleSetsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _userRuleSetsGrid.CellValueChanged += (_, _) => SaveRuleSetsFromGrid();
        _userRuleSetsGrid.DataError += (_, _) => { };

        _dnsModeCombo.SelectedIndexChanged += (_, _) =>
        {
            _connectionSettings.UpdateDohFieldsEnabled();
            if (_dnsUiReady) RestartConnectionSettingsDebounce();
        };
        _dnsPresetCombo.SelectedIndexChanged += (_, _) =>
        {
            ApplyDnsPresetToBoxes();
            if (_dnsUiReady) RestartConnectionSettingsDebounce();
        };
        _dnsDetourCombo.SelectedIndexChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _dohServerBox.TextChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _dohPathBox.TextChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _dohSniBox.TextChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _proxyOverrideBox.TextChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _tunInterfaceNameBox.TextChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _tunAddressCidrModeCombo.SelectedIndexChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _tunAddressCidrBox.TextChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _tunMtu.ValueChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _tunStackCombo.SelectedIndexChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _tunAutoRoute.CheckedChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };
        _tunStrictRoute.CheckedChanged += (_, _) => { if (_dnsUiReady) RestartConnectionSettingsDebounce(); };

        _vpnConnectionService.ConnectionStateChanged += (_, connected) =>
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (_connectionUiUpdateQueued) return;
                _connectionUiUpdateQueued = true;
                BeginInvoke(() =>
                {
                    UpdateButtons();
                    NotifyVpnConnectionState(connected);
                    _connectionUiUpdateQueued = false;
                });
            }
            catch
            {
                _connectionUiUpdateQueued = false;
                // ignore
            }
        };

        _updatePeriodicTimer = new System.Windows.Forms.Timer { Interval = 86_400_000 };
        _updatePeriodicTimer.Tick += (_, _) => { _ = OnPeriodicUpdateCheckAsync(); };
        Shown += (_, _) =>
        {
            _updatePeriodicTimer.Start();
            _ = RunStartupUpdatesAsync();
        };

        UiStyler.ApplyToForm(this);
        ResumeLayout(false);
        PerformLayout();
        LoadData();
        UpdateButtons();

        FormClosing += (_, e) =>
        {
            // MainForm may be hidden-to-tray by outer controller; default close is allowed.
            _logTimer.Stop();
            _updatePeriodicTimer.Stop();
        };
    }

    public void ApplyStartup(StartupArgs? startup)
    {
        if (startup is null) return;

        if (!string.IsNullOrWhiteSpace(startup.Mode))
        {
            _state.Mode = ConnectionPolicy.NormalizeMode(startup.Mode);
            SaveState();
        }

        if (!string.IsNullOrWhiteSpace(startup.ProfileId))
        {
            var match = _profiles.FirstOrDefault(p => string.Equals(p.Id, startup.ProfileId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                _profilesCombo.SelectedItem = match;
        }

        _modeCombo.SelectedIndex = ModeToComboIndex(_state.Mode);
        SyncTunAppsListFromState();
        UpdateConnectionTabVisibility();
        UpdateTitle();
        UpdateButtons();

        if (startup.AutoStart)
        {
            BeginInvoke(async () =>
            {
                try
                {
                    await StartAsync();
                }
                catch
                {
                    // StartAsync already shows errors; ignore.
                }
            });
        }
    }

    public void ConnectFromTray()
    {
        void Go()
        {
            if (_vpnConnectionService.GetStatus().IsRunning) return;
            _ = StartAsync();
        }

        if (InvokeRequired)
            BeginInvoke(Go);
        else
            Go();
    }

    public void DisconnectFromTray()
    {
        void Go()
        {
            if (!_vpnConnectionService.GetStatus().IsRunning) return;
            _ = StopAsync();
        }

        if (InvokeRequired)
            BeginInvoke(Go);
        else
            Go();
    }

    public void Shutdown()
    {
        try
        {
            DisconnectVpnSync();
            _logTimer.Stop();
            NotifyVpnConnectionState(false);
        }
        catch
        {
            // best-effort
        }
    }

    private void DisconnectVpnSync()
    {
        _vpnConnectionService.DisconnectAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public void ReloadProfilesFromSubscriptions()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ReloadProfilesFromSubscriptions);
            return;
        }

        LoadData();
        UpdateButtons();
    }

    private void LoadData()
    {
        _profiles = _profileService.GetProfiles();
        _state = _settingsService.GetState();

        _profilesCombo.DataSource = _profiles.ToList();
        _profilesCombo.DisplayMember = nameof(VpnProfile.Name);

        var active = _profiles.FirstOrDefault(p => p.Id == _state.ActiveProfileId) ?? _profiles.FirstOrDefault();
        if (active is not null)
        {
            _profilesCombo.SelectedItem = active;
            _state.ActiveProfileId = active.Id;
            SaveState();
        }
        else if (!string.IsNullOrWhiteSpace(_state.ActiveProfileId))
        {
            _state.ActiveProfileId = string.Empty;
            SaveState();
        }

        _port.Value = Math.Clamp(_state.LocalMixedPort, 1, 65535);
        if (_state.TunAppProcessPaths is null)
            _state.TunAppProcessPaths = new List<string>();
        if (_state.UserRuleSets is null)
            _state.UserRuleSets = new List<UserRuleSetModel>();
        if (string.IsNullOrWhiteSpace(_state.DnsDetour))
            _state.DnsDetour = "direct";
        _modeCombo.SelectedIndex = ModeToComboIndex(_state.Mode);
        SyncTunAppsListFromState();
        SyncRuleSetsGridFromState();
        UpdateConnectionTabVisibility();
        _debugLogs.Checked = _state.DebugLogs;
        if (_logFilterCombo.SelectedIndex < 0) _logFilterCombo.SelectedIndex = 2; // INFO
        UpdateSingBoxHashLabel();
        _connectionSettings.LoadFromState(_state);
        _dnsUiReady = true;
        UpdateTitle();
    }

    private void SaveState()
    {
        _settingsService.SaveState(_state);
    }

    private void RestartConnectionSettingsDebounce()
    {
        _dnsDebounceTimer.Stop();
        _dnsDebounceTimer.Start();
    }

    private void ApplyDnsPresetToBoxes()
    {
        try
        {
            var idx = _dnsPresetCombo.SelectedIndex;
            if (idx < 0) return;

            // Keep path stable for common DoH providers.
            if (string.IsNullOrWhiteSpace(_dohPathBox.Text))
                _dohPathBox.Text = "/dns-query";

            switch (idx)
            {
                case 0: // Cloudflare
                    _dohServerBox.Text = "1.1.1.1";
                    _dohSniBox.Text = "cloudflare-dns.com";
                    _dohPathBox.Text = "/dns-query";
                    break;
                case 1: // Google
                    _dohServerBox.Text = "8.8.8.8";
                    _dohSniBox.Text = "dns.google";
                    _dohPathBox.Text = "/dns-query";
                    break;
                case 2: // Quad9
                    _dohServerBox.Text = "9.9.9.9";
                    _dohSniBox.Text = "dns.quad9.net";
                    _dohPathBox.Text = "/dns-query";
                    break;
                case 3: // AdGuard
                    _dohServerBox.Text = "94.140.14.14";
                    _dohSniBox.Text = "dns.adguard.com";
                    _dohPathBox.Text = "/dns-query";
                    break;
                default:
                    break;
            }
        }
        catch
        {
            // ignore
        }
    }

    private void SaveConnectionSettingsFromUi(bool showDialogs)
    {
        try
        {
            var proxy = new ProxyConnectionSettings
            {
                ProxyOverride = _proxyOverrideBox.Text
            };
            ProxyConnectionPolicy.Validate(proxy);
            ConnectionSettingsMapper.ApplyProxySettings(_state, proxy);

            var tun = new TunSettings
            {
                InterfaceName = _tunInterfaceNameBox.Text,
                AddressCidr = _tunAddressCidrModeCombo.SelectedIndex == 0
                    ? "auto"
                    : (_tunAddressCidrBox.Text ?? "").Trim(),
                Mtu = (int)_tunMtu.Value,
                Stack = TunSettingsPolicy.ComboIndexToStack(_tunStackCombo.SelectedIndex),
                AutoRoute = _tunAutoRoute.Checked,
                StrictRoute = _tunStrictRoute.Checked
            };
            TunSettingsPolicy.Validate(tun);
            ConnectionSettingsMapper.ApplyTunSettings(_state, tun);

            var dnsMode = _dnsModeCombo.SelectedIndex == 0 ? "system" : "doh";
            var detour = DnsPolicy.ComboIndexToDetour(_dnsDetourCombo.SelectedIndex);

            var dns = ConnectionSettingsMapper.ToDnsSettings(_state);
            dns.Mode = dnsMode;
            dns.Detour = detour;

            if (dnsMode == "doh")
            {
                var server = (_dohServerBox.Text ?? "").Trim();
                var path = (_dohPathBox.Text ?? "").Trim();
                var sni = (_dohSniBox.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(server))
                {
                    if (showDialogs) throw new InvalidOperationException("DoH IP не задан.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(path))
                    path = "/dns-query";
                if (string.IsNullOrWhiteSpace(sni))
                {
                    if (showDialogs) throw new InvalidOperationException("DoH SNI не задан (нужен для TLS).");
                    return;
                }

                dns.DohServer = server;
                dns.DohPath = path;
                dns.DohSni = sni;
            }

            DnsPolicy.Normalize(dns);
            ConnectionSettingsMapper.ApplyDnsSettings(_state, dns);
            SaveState();

            ShowDnsNotice("Настройки соединения сохранены. Вступят в силу после переподключения.");
        }
        catch (Exception ex)
        {
            if (showDialogs)
                MessageBox.Show(this, ex.Message, "Соединение", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowDnsNotice(string text)
    {
        try
        {
            _dnsNotice.Text = text;
            _dnsNotice.Visible = true;

            var t = new System.Windows.Forms.Timer { Interval = 2800 };
            t.Tick += (_, _) =>
            {
                t.Stop();
                t.Dispose();
                if (IsDisposed) return;
                _dnsNotice.Visible = false;
            };
            t.Start();
        }
        catch
        {
            // ignore
        }
    }

    private static DataGridView CreateRuleSetsDataGridView(bool multiSelect)
    {
        var g = new BufferedDataGridView
        {
            Dock = DockStyle.Fill,
            Height = 96,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = multiSelect,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            AutoGenerateColumns = false
        };
        g.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Вкл",
            DataPropertyName = nameof(UserRuleSetModel.Enabled),
            Width = 44
        });
        g.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Имя",
            DataPropertyName = nameof(UserRuleSetModel.Name),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120
        });
        g.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Действие",
            DataPropertyName = nameof(UserRuleSetModel.Action),
            Width = 90,
            FlatStyle = FlatStyle.Flat,
            DataSource = new[] { "direct", "block" }
        });
        g.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Файл",
            DataPropertyName = nameof(UserRuleSetModel.FileName),
            Width = 200,
            ReadOnly = true
        });
        return g;
    }

    private void SyncRuleSetsGridFromState()
    {
        var all = _state.UserRuleSets ?? new List<UserRuleSetModel>();
        _builtinRuleSetsBinding = new BindingList<UserRuleSetModel>(
            all.Where(x => !string.IsNullOrWhiteSpace(x.BuiltinId)).ToList());
        _userRuleSetsBinding = new BindingList<UserRuleSetModel>(
            all.Where(x => string.IsNullOrWhiteSpace(x.BuiltinId)).ToList());
        _builtinRuleSetsGrid.DataSource = _builtinRuleSetsBinding;
        _userRuleSetsGrid.DataSource = _userRuleSetsBinding;
        RefreshBuiltinGridRowStyles();
        UpdateBuiltinFetchOrRemoveButton();
    }

    private void SaveRuleSetsFromGrid()
    {
        try
        {
            if (_state.UserRuleSets is null) _state.UserRuleSets = new List<UserRuleSetModel>();
            _state.UserRuleSets = _builtinRuleSetsBinding.Concat(_userRuleSetsBinding).ToList();
            SaveState();
        }
        catch
        {
            // best-effort
        }
    }

    private bool RuleSetFileExists(UserRuleSetModel rs)
    {
        var name = (rs.FileName ?? "").Trim();
        if (name.Length == 0) return false;
        return File.Exists(Path.Combine(_paths.RuleSetsDir, name));
    }

    private void RefreshBuiltinGridRowStyles()
    {
        var normalFg = _builtinRuleSetsGrid.DefaultCellStyle.ForeColor;
        if (normalFg.IsEmpty)
            normalFg = SystemColors.ControlText;
        foreach (DataGridViewRow row in _builtinRuleSetsGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.DataBoundItem is not UserRuleSetModel rs) continue;
            var dim = !RuleSetFileExists(rs);
            row.DefaultCellStyle.ForeColor = dim ? SystemColors.GrayText : normalFg;
        }
    }

    private List<UserRuleSetModel> GetSelectedBuiltinRuleSets()
    {
        var list = new List<UserRuleSetModel>();
        foreach (DataGridViewRow row in _builtinRuleSetsGrid.SelectedRows)
        {
            if (row.DataBoundItem is UserRuleSetModel rs)
                list.Add(rs);
        }

        return list;
    }

    private void UpdateBuiltinFetchOrRemoveButton()
    {
        try
        {
            var selected = GetSelectedBuiltinRuleSets();
            if (selected.Count == 0)
            {
                _builtinRuleSetsFetchOrRemoveBtn.Enabled = false;
                _builtinRuleSetsFetchOrRemoveBtn.Text = "Скачать";
                return;
            }

            var anyMissingFile = selected.Exists(rs => !RuleSetFileExists(rs));
            _builtinRuleSetsFetchOrRemoveBtn.Enabled = true;
            _builtinRuleSetsFetchOrRemoveBtn.Text = anyMissingFile ? "Скачать" : "Удалить с диска";
        }
        catch
        {
            _builtinRuleSetsFetchOrRemoveBtn.Enabled = false;
        }
    }

    private void AddRuleSet()
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Добавить rule-set (.srs)",
                Filter = "Sing-box rule-set (*.srs)|*.srs|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            var src = ofd.FileName;
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
                throw new FileNotFoundException("Файл не найден.");
            if (!src.EndsWith(".srs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Поддерживаются только файлы .srs.");

            Directory.CreateDirectory(_paths.RuleSetsDir);

            var baseName = Path.GetFileNameWithoutExtension(src);
            var fileName = Path.GetFileName(src);
            var dest = Path.Combine(_paths.RuleSetsDir, fileName);
            if (File.Exists(dest))
            {
                var suffix = Guid.NewGuid().ToString("N")[..8];
                fileName = $"{baseName}-{suffix}.srs";
                dest = Path.Combine(_paths.RuleSetsDir, fileName);
            }

            File.Copy(src, dest, overwrite: false);

            var tag = $"user-ruleset-{Guid.NewGuid():N}"[..("user-ruleset-".Length + 12)];
            _userRuleSetsBinding.Add(new UserRuleSetModel
            {
                Tag = tag,
                Name = string.IsNullOrWhiteSpace(baseName) ? fileName : baseName,
                FileName = fileName,
                Enabled = true,
                Action = "direct"
            });
            SaveRuleSetsFromGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Добавить rule-set не удалось", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RemoveSelectedUserRuleSet()
    {
        try
        {
            if (_userRuleSetsGrid.CurrentRow?.DataBoundItem is not UserRuleSetModel item) return;
            var idx = _userRuleSetsBinding.IndexOf(item);
            if (idx < 0) return;
            _userRuleSetsBinding.RemoveAt(idx);
            SaveRuleSetsFromGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Удалить rule-set не удалось", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RemoveBuiltinFilesForList(IReadOnlyList<UserRuleSetModel> targets)
    {
        try
        {
            if (targets.Count == 0) return;

            foreach (var rs in targets)
            {
                var path = Path.Combine(_paths.RuleSetsDir, (rs.FileName ?? "").Trim());
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Удалить файл не удалось", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                rs.RemoteEtag = null;
                rs.LastDownloadedUtc = null;
                rs.Enabled = false;
            }

            SaveRuleSetsFromGrid();
            RefreshBuiltinGridRowStyles();
            UpdateBuiltinFetchOrRemoveButton();
            _builtinRuleSetsGrid.ClearSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Rule set", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnBuiltinFetchOrRemoveClickAsync()
    {
        var selected = GetSelectedBuiltinRuleSets();
        if (selected.Count == 0) return;

        var needDownload = selected.Where(rs => !RuleSetFileExists(rs)).Distinct().ToList();
        if (needDownload.Count > 0)
        {
            await DownloadBuiltinListAsync(needDownload).ConfigureAwait(true);
            return;
        }

        RemoveBuiltinFilesForList(selected);
    }

    private void BuiltinRuleSetsGrid_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_builtinRuleSetsBinding.Count <= e.RowIndex) return;
        var rs = _builtinRuleSetsBinding[e.RowIndex];

        var col = _builtinRuleSetsGrid.Columns[e.ColumnIndex];
        if (col is DataGridViewTextBoxColumn && col.DataPropertyName == nameof(UserRuleSetModel.Name))
        {
            e.Cancel = true;
            return;
        }

        if (col is DataGridViewCheckBoxColumn && col.DataPropertyName == nameof(UserRuleSetModel.Enabled))
        {
            if (RuleSetFileExists(rs))
                return;

            if (rs.Enabled)
                return;

            e.Cancel = true;
            var dr = MessageBox.Show(this,
                "Этот список ещё не загружен. Скачать и включить?",
                "Rule set",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            if (dr != DialogResult.Yes)
                return;

            DownloadAndEnableBuiltinAfterConsentAsync(rs);
        }
    }

    private async void DownloadAndEnableBuiltinAfterConsentAsync(UserRuleSetModel rs)
    {
        try
        {
            SetRuleSetDownloadUiBusy(true);
            if (!await DownloadBuiltinFileIfNeededAsync(rs, showErrorsOnFailure: true).ConfigureAwait(true))
                return;

            rs.Enabled = true;
            SaveRuleSetsFromGrid();
            SyncRuleSetsGridFromState();
            RefreshBuiltinGridRowStyles();
            UpdateBuiltinFetchOrRemoveButton();
        }
        catch (Exception ex)
        {
            _appLogger.Error("app/rulesets", ex, "Скачать и включить rule-set не удалось.");
            MessageBox.Show(this, ex.Message, "Rule set", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetRuleSetDownloadUiBusy(false);
        }
    }

    /// <summary>Скачивает .srs с сервера, если файла ещё нет. Не трогает Enabled.</summary>
    private async Task<bool> DownloadBuiltinFileIfNeededAsync(UserRuleSetModel rs, bool showErrorsOnFailure)
    {
        if (RuleSetFileExists(rs))
            return true;

        var def = BuiltinGeositeRuleSets.FindByBuiltinId(rs.BuiltinId);
        if (def is null)
            return false;

        var dest = Path.Combine(_paths.RuleSetsDir, rs.FileName.Trim());
        var result = await RuleSetRemoteDownloader.DownloadAsync(def.DownloadUrl, dest, ifNoneMatch: null, CancellationToken.None)
            .ConfigureAwait(true);
        if (!result.Ok)
        {
            _appLogger.Warn("app/rulesets", $"Download builtin failed: {rs.BuiltinId}, {result.Error}");
            if (showErrorsOnFailure)
            {
                MessageBox.Show(this,
                    $"Не удалось скачать «{rs.Name ?? rs.FileName}».\n{result.Error}",
                    "Rule set",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return false;
        }

        if (!result.NotModified)
        {
            if (!string.IsNullOrWhiteSpace(result.NewEtag))
                rs.RemoteEtag = result.NewEtag.Trim();
            rs.LastDownloadedUtc = DateTimeOffset.UtcNow;
        }

        return true;
    }

    private static void OpenOtherRuleSetLists()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = BuiltinGeositeRuleSets.CatalogBrowserUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    private async Task DownloadBuiltinListAsync(IReadOnlyList<UserRuleSetModel> pending)
    {
        try
        {
            if (pending.Count == 0)
                return;

            SetRuleSetDownloadUiBusy(true);
            foreach (var rs in pending)
            {
                if (!await DownloadBuiltinFileIfNeededAsync(rs, showErrorsOnFailure: true).ConfigureAwait(true))
                    return;
            }

            SaveRuleSetsFromGrid();
            SyncRuleSetsGridFromState();
            RefreshBuiltinGridRowStyles();
            UpdateBuiltinFetchOrRemoveButton();
            _appLogger.Info("app/rulesets", $"Builtin rule-sets saved: {pending.Count}");
        }
        catch (Exception ex)
        {
            _appLogger.Error("app/rulesets", ex, "Скачать встроенный rule-set не удалось.");
            MessageBox.Show(this, ex.Message, "Rule set", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetRuleSetDownloadUiBusy(false);
        }
    }

    private async Task CheckBuiltinRuleSetUpdatesAsync()
    {
        try
        {
            var targets = (_state.UserRuleSets ?? new List<UserRuleSetModel>())
                .Where(x => !string.IsNullOrWhiteSpace(x.BuiltinId))
                .ToList();
            if (targets.Count == 0) return;

            var anyFile = targets.Any(x =>
            {
                var p = Path.Combine(_paths.RuleSetsDir, (x.FileName ?? "").Trim());
                return File.Exists(p);
            });
            if (!anyFile)
            {
                MessageBox.Show(this,
                    "Нет скачанных встроенных файлов. Включите список галочкой (будет предложена загрузка) или нажмите «Скачать».",
                    "Обновления rule-set",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SetRuleSetDownloadUiBusy(true);
            var unchanged = 0;
            var updated = 0;
            var failed = new List<string>();

            foreach (var rs in targets)
            {
                var def = BuiltinGeositeRuleSets.FindByBuiltinId(rs.BuiltinId);
                if (def is null) continue;

                var dest = Path.Combine(_paths.RuleSetsDir, rs.FileName.Trim());
                if (!File.Exists(dest))
                    continue;

                var useConditional = !string.IsNullOrWhiteSpace(rs.RemoteEtag);
                var result = await RuleSetRemoteDownloader.DownloadAsync(
                    def.DownloadUrl,
                    dest,
                    ifNoneMatch: useConditional ? rs.RemoteEtag : null,
                    CancellationToken.None).ConfigureAwait(true);

                if (!result.Ok)
                {
                    failed.Add($"{def.DisplayName}: {result.Error}");
                    continue;
                }

                if (result.NotModified)
                {
                    unchanged++;
                    continue;
                }

                ApplyDownloadResultToRuleSet(rs, result.NewEtag);
                updated++;
            }

            if (failed.Count != 0)
            {
                MessageBox.Show(this,
                    "Часть проверок завершилась ошибкой:\n- " + string.Join("\n- ", failed),
                    "Обновления rule-set",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var msg = updated == 0
                ? "Все проверенные списки уже актуальны."
                : $"Обновлено файлов: {updated}. Без изменений: {unchanged}.";
            MessageBox.Show(this, msg, "Обновления rule-set", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _appLogger.Info("app/rulesets", $"Rule-set update check: updated={updated}, unchanged={unchanged}");
        }
        catch (Exception ex)
        {
            _appLogger.Error("app/rulesets", ex, "Проверка обновлений rule-set не удалась.");
            MessageBox.Show(this, ex.Message, "Обновления rule-set", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetRuleSetDownloadUiBusy(false);
        }
    }

    private void ApplyDownloadResultToRuleSet(UserRuleSetModel rs, string? newEtag)
    {
        if (!string.IsNullOrWhiteSpace(newEtag))
            rs.RemoteEtag = newEtag.Trim();
        rs.LastDownloadedUtc = DateTimeOffset.UtcNow;
        SaveState();
        SyncRuleSetsGridFromState();
    }

    private void SetRuleSetDownloadUiBusy(bool busy)
    {
        _builtinRuleSetsCheckUpdatesBtn.Enabled = !busy;
        _builtinRuleSetsOtherListsBtn.Enabled = !busy;
        if (busy)
        {
            _builtinRuleSetsFetchOrRemoveBtn.Enabled = false;
            Cursor = Cursors.WaitCursor;
        }
        else
        {
            Cursor = Cursors.Default;
            UpdateBuiltinFetchOrRemoveButton();
        }
    }

    private void CopyLogsToClipboard()
    {
        try
        {
            var text = _logBox.Text ?? "";
            if (text.Length == 0) return;
            Clipboard.SetText(text, TextDataFormat.Text);
            _appLogger.Debug("app/ui", "Логи скопированы в буфер обмена.");
        }
        catch (Exception ex)
        {
            _appLogger.Error("app/ui", ex, "Копирование логов завершилось ошибкой.");
            MessageBox.Show(this, ex.Message, "Копирование не удалось", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DownloadLogs()
    {
        try
        {
            var all = _logStore.SnapshotAll();
            if (all.Length == 0)
            {
                MessageBox.Show(this, "Логи пустые.", "Скачать логи", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Title = "Скачать логи",
                Filter = "Текст (*.txt)|*.txt|Все файлы (*.*)|*.*",
                FileName = $"nothingvpn-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt",
                OverwritePrompt = true
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(sfd.FileName, all);
            _appLogger.Info("app/ui", $"Логи экспортированы: {Path.GetFileName(sfd.FileName)}");
        }
        catch (Exception ex)
        {
            _appLogger.Error("app/ui", ex, "Экспорт логов завершился ошибкой.");
            MessageBox.Show(this, ex.Message, "Скачать логи не удалось", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task PingAsync()
    {
        if (!_vpnConnectionService.GetStatus().IsRunning)
        {
            MessageBox.Show(this, "Сначала нажмите «Старт».", "Пинг", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var isTun = ConnectionPolicy.IsTunMode(_state.Mode);
        var isTunApps = string.Equals(_state.Mode, "tun_apps", StringComparison.OrdinalIgnoreCase);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!isTun)
            {
                var r = await _diagnosticsService.RunProxySmokeTestAsync(
                    targetHost: "api.ipify.org",
                    targetPort: 443,
                    timeout: TimeSpan.FromSeconds(8));

                sw.Stop();
                if (!r.Success)
                    throw new InvalidOperationException(r.Error ?? "Proxy test failed.");

                MessageBox.Show(this, $"Прокси: OK\nВремя: {sw.ElapsedMilliseconds} мс", "Пинг", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (isTunApps)
            {
                // Трафик этого процесса обычно не в списке — ipify проверяет «direct», не VLESS.
                var r = await _diagnosticsService.RunTunSmokeTestAsync(TimeSpan.FromSeconds(4));
                sw.Stop();
                if (!r.Success)
                    throw new InvalidOperationException(r.Error ?? "TUN test failed.");

                MessageBox.Show(this,
                    $"Связность: OK\nВремя: {sw.ElapsedMilliseconds} мс\n\nВажно: в режиме «TUN (выбранные приложения)» тест идёт из процесса Nothing VPN (обычно напрямую, без VLESS). OK здесь не означает, что выбранные .exe ходят в интернет через туннель — проверяйте сами браузер/игру из списка.",
                    "Пинг",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // TUN (весь трафик): проверка выхода в интернет через туннель.
            var r2 = await _diagnosticsService.RunTunSmokeTestAsync(TimeSpan.FromSeconds(4));
            sw.Stop();
            if (!r2.Success)
                throw new InvalidOperationException(r2.Error ?? "TUN test failed.");

            MessageBox.Show(this, $"TUN: OK\nВремя: {sw.ElapsedMilliseconds} мс", "Пинг", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var mode = isTun ? "TUN" : "Прокси";
            _appLogger.Warn("app/smoke", $"{mode} smoke test: FAIL, {sw.ElapsedMilliseconds} ms, reason: {ex.Message}");
            MessageBox.Show(this, $"{mode}: FAIL\n{ex.Message}\nВремя: {sw.ElapsedMilliseconds} мс", "Пинг", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateTitle()
    {
        var modeLabel = _state.Mode.ToLowerInvariant() switch
        {
            "tun" => "TUN",
            "tun_apps" => "TUN (приложения)",
            _ => "прокси"
        };
        Text = $"Nothing VPN ({modeLabel})";
    }

    private void TrustCurrentSingBox()
    {
        try
        {
            // Same resolver logic as runner uses: simplest is to expect it next to app for now.
            var baseDir = AppContext.BaseDirectory;
            var exe = Path.Combine(baseDir, "sing-box.exe");
            if (!File.Exists(exe))
            {
                MessageBox.Show(this,
                    "sing-box.exe not found next to the app.\nPut it into the same folder as the EXE and try again.",
                    "Trust failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var sha = FileHash.Sha256Hex(exe);
            _state.TrustedSingBoxSha256 = sha;
            SaveState();
            UpdateSingBoxHashLabel();
            MessageBox.Show(this, $"Trusted SHA-256:\n{sha}", "Trusted", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Trust failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateSingBoxHashLabel()
    {
        _singBoxHashLabel.Text = string.IsNullOrWhiteSpace(_state.TrustedSingBoxSha256)
            ? "hash: (not set)"
            : $"hash: {_state.TrustedSingBoxSha256[..Math.Min(12, _state.TrustedSingBoxSha256.Length)]}…";
    }

    private void UpdateButtons()
    {
        var running = _vpnConnectionService.GetStatus().IsRunning;
        _startBtn.Enabled = !running && _profilesCombo.SelectedItem is VpnProfile;
        _stopBtn.Enabled = running;
        _profilesBtn.Enabled = !running;
        _port.Enabled = !running;
        _modeCombo.Enabled = !running;
        var editTunApps = !running && string.Equals(_state.Mode, "tun_apps", StringComparison.OrdinalIgnoreCase);
        _tunAppsList.Enabled = editTunApps;
        _tunAppsAddBtn.Enabled = editTunApps;
        _tunAppsBrowseFileBtn.Enabled = editTunApps;
        _tunAppsRemoveBtn.Enabled = editTunApps;

        _statusValue.Text = running ? "Запущено" : "Остановлено";
        _adminValue.Text = _appLifecycleService.IsAdministrator() ? "Администратор" : "Обычный пользователь";
        _modeValue.Text = _state.Mode.ToLowerInvariant() switch
        {
            "tun" => "TUN (весь трафик)",
            "tun_apps" => "TUN (выбранные приложения)",
            _ => "Прокси"
        };
        _profileValue.Text = _profilesCombo.SelectedItem is VpnProfile p ? p.Name : "(не выбран)";
        _portValue.Text = _state.LocalMixedPort.ToString();
        _dnsValue.Text = BuildDnsStatusText();
        _ruleSetsValue.Text = BuildRuleSetsStatusText();
        _tunValue.Text = BuildTunStatusText();
        _proxyBypassValue.Text = BuildProxyBypassStatusText();
        _connectionSettings.SetConnectionFieldsEnabled(!running);
    }

    private string BuildDnsStatusText()
    {
        var mode = (_state.DnsMode ?? "").Trim().ToLowerInvariant();
        var detour = (_state.DnsDetour ?? "direct").Trim().ToLowerInvariant();
        var detourLabel = DnsPolicy.DetourToDisplayLabel(detour).ToLowerInvariant();
        if (mode == "doh")
        {
            var server = string.IsNullOrWhiteSpace(_state.DohServer) ? "(не задан)" : _state.DohServer.Trim();
            var sni = string.IsNullOrWhiteSpace(_state.DohSni) ? "(без SNI)" : _state.DohSni.Trim();
            return $"DoH: {server}, SNI: {sni}, {detourLabel}";
        }

        return $"Системный/по умолчанию, {detourLabel}";
    }

    private string BuildRuleSetsStatusText()
    {
        var all = _state.UserRuleSets ?? new List<UserRuleSetModel>();
        var enabled = all.Count(x => x.Enabled);
        var builtinEnabled = all.Count(x => x.Enabled && !string.IsNullOrWhiteSpace(x.BuiltinId));
        var customEnabled = all.Count(x => x.Enabled && string.IsNullOrWhiteSpace(x.BuiltinId));
        return $"{enabled} активных (встроенные: {builtinEnabled}, пользовательские: {customEnabled})";
    }

    private string BuildTunStatusText()
    {
        if (!ConnectionPolicy.IsTunMode(_state.Mode))
            return "—";

        var mtu = TunSettingsPolicy.NormalizeMtu(_state.TunMtu);
        var stack = TunSettingsPolicy.StackToDisplayLabel(_state.TunStack);
        var strict = _state.TunStrictRoute ? "вкл." : "выкл.";
        return $"MTU {mtu}, стек {stack}, строгая маршрутизация {strict}";
    }

    private string BuildProxyBypassStatusText()
    {
        if (!string.Equals(_state.Mode, ConnectionPolicy.ProxyMode, StringComparison.OrdinalIgnoreCase))
            return "—";

        var value = (_state.ProxyOverride ?? "").Trim();
        if (value.Length == 0)
            return ProxyConnectionPolicy.DefaultProxyOverride;

        return value.Length > 48 ? value[..48] + "…" : value;
    }

    private void OpenProfilesDialog()
    {
        try
        {
            using var dlg = new ProfilesDialog(_profileService, _subscriptionService, _state.ActiveProfileId);
            dlg.ShowDialog(this);

            if (dlg.ResultActiveProfileId is not null)
            {
                _state.ActiveProfileId = dlg.ResultActiveProfileId;
                SaveState();
            }

            LoadData();
            UpdateButtons();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Профили", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StartAsync()
    {
        if (_profilesCombo.SelectedItem is not VpnProfile p) return;

        try
        {
            UpdateButtons();
            var result = await _vpnConnectionService.ConnectAsync(new ConnectRequest { ProfileId = p.Id });
            if (result.RequiresElevation)
            {
                var ok = _appLifecycleService.RestartElevated(result.ElevationArgs ?? _appLifecycleService.BuildTakeoverArgs(_state.Mode, p.Id));
                if (!ok)
                    throw new InvalidOperationException("TUN requires Administrator privileges (UAC was cancelled).");
                BeginInvoke(_requestExit);
                return;
            }

            _logTimer.Start();
            NotifyVpnConnectionState(true);
        }
        catch (Exception ex)
        {
            _appLogger.Error("app/runtime", ex, "Запуск VPN завершился ошибкой.");
            MessageBox.Show(this, ex.Message, "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            await StopAsync();
        }
        finally
        {
            UpdateButtons();
        }
    }

    private void ValidateUserRuleSets()
    {
        var missing = new List<string>();
        var bad = new List<string>();
        var dupTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rs in _state.UserRuleSets ?? new List<UserRuleSetModel>())
        {
            if (!rs.Enabled) continue;
            if (string.IsNullOrWhiteSpace(rs.FileName) || string.IsNullOrWhiteSpace(rs.Tag))
            {
                bad.Add(rs.Name?.Trim().Length > 0 ? rs.Name : "(без имени)");
                continue;
            }

            var tag = rs.Tag.Trim();
            if (!seenTags.Add(tag))
                dupTags.Add(tag);

            var action = (rs.Action ?? "").Trim().ToLowerInvariant();
            if (action != "direct" && action != "block")
            {
                bad.Add(rs.Name?.Trim().Length > 0 ? rs.Name : rs.Tag);
                continue;
            }

            var fileName = rs.FileName.Trim();
            if (!IsSafeRuleSetFileName(fileName))
            {
                bad.Add(rs.Name?.Trim().Length > 0 ? rs.Name : rs.Tag);
                continue;
            }

            var full = Path.Combine(_paths.RuleSetsDir, fileName);
            if (!fileName.EndsWith(".srs", StringComparison.OrdinalIgnoreCase))
            {
                bad.Add(rs.Name?.Trim().Length > 0 ? rs.Name : rs.Tag);
                continue;
            }
            if (!File.Exists(full))
            {
                var line = $"{(string.IsNullOrWhiteSpace(rs.Name) ? rs.Tag : rs.Name)} → {rs.FileName}";
                if (!string.IsNullOrWhiteSpace(rs.BuiltinId))
                    line += " (скачайте встроенный список или отключите строку)";
                missing.Add(line);
            }
        }

        if (bad.Count != 0)
            throw new InvalidOperationException("Некоторые rule-set записи повреждены (нет tag/filename). Удалите их и добавьте заново:\n- " + string.Join("\n- ", bad));

        if (dupTags.Count != 0)
            throw new InvalidOperationException("Найдены дублирующиеся rule-set tag (должны быть уникальными). Удалите дубликаты и добавьте заново:\n- " + string.Join("\n- ", dupTags.OrderBy(x => x)));

        if (missing.Count != 0)
            throw new InvalidOperationException("Не найдены файлы включённых rule-set (.srs). Проверьте, что файлы на месте или добавьте заново:\n- " + string.Join("\n- ", missing));
    }

    private static bool IsSafeRuleSetFileName(string fileName)
    {
        var raw = (fileName ?? "").Trim();
        if (raw.Length == 0) return false;
        if (Path.IsPathRooted(raw)) return false;
        var safe = Path.GetFileName(raw);
        if (!string.Equals(safe, raw, StringComparison.Ordinal)) return false;
        if (safe.Contains("..", StringComparison.Ordinal)) return false;
        return true;
    }

    private void NotifyVpnConnectionState(bool connected)
    {
        try { _vpnConnectionStateChanged?.Invoke(connected); } catch { }
    }

    private async Task StopAsync()
    {
        _stopBtn.Enabled = false;
        _startBtn.Enabled = false;
        try
        {
            await _vpnConnectionService.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _appLogger.Error("app/runtime", ex, "Остановка VPN завершилась ошибкой.");
            if (!IsDisposed && IsHandleCreated)
            {
                if (InvokeRequired)
                    BeginInvoke(() => MessageBox.Show(this, ex.Message, "Стоп", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                else
                    MessageBox.Show(this, ex.Message, "Стоп", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _logTimer.Stop();
            NotifyVpnConnectionState(false);
            if (!IsDisposed && IsHandleCreated)
            {
                void UiFinish()
                {
                    RefreshLog();
                    UpdateButtons();
                }

                if (InvokeRequired)
                    BeginInvoke(UiFinish);
                else
                    UiFinish();
            }
        }
    }

    // Logs are in-memory; export via "Скачать…" on the Logs tab.

    private void RefreshLog()
    {
        try
        {
            // Don't do file IO unless user is actually viewing logs.
            if (_tabs.SelectedTab != _tabLogs) return;

            var min = SelectedMinLevel();
            if (min == _lastLogMinLevel && _logStore.TryGetVersion(out var currentVer) && currentVer == _lastLogVersion)
                return;

            var text = _logStore.SnapshotText(min, out var ver);
            if (ver == _lastLogVersion && min == _lastLogMinLevel) return;
            _lastLogVersion = ver;
            _lastLogMinLevel = min;
            // Keep TextBox payload bounded to avoid heavy UI updates over time.
            const int maxLogChars = 300_000;
            if (text.Length > maxLogChars)
                text = text[^maxLogChars..];

            var current = _logBox.Text;
            if (text.StartsWith(current, StringComparison.Ordinal))
                _logBox.AppendText(text[current.Length..]);
            else
                _logBox.Text = text;

            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
        catch
        {
            // ignore
        }
    }

    private int SelectedMinLevel()
    {
        return _logFilterCombo.SelectedIndex switch
        {
            0 => 0, // TRACE
            1 => 1, // DEBUG
            3 => 3, // WARN
            4 => 4, // ERROR
            _ => 2  // INFO
        };
    }

    private static int ModeToComboIndex(string? mode) =>
        (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "tun" => 1,
            "tun_apps" => 2,
            _ => 0
        };

    private static string ComboIndexToMode(int idx) => idx switch
    {
        1 => "tun",
        2 => "tun_apps",
        _ => "proxy"
    };

    private void UpdateConnectionTabVisibility()
    {
        _connectionSettings.UpdateVisibility(_state.Mode);

        var tunAppsVisible = string.Equals(_state.Mode, ConnectionPolicy.TunAppsMode, StringComparison.OrdinalIgnoreCase);
        if (_tunAppsGroup.Visible != tunAppsVisible)
            _tunAppsGroup.Visible = tunAppsVisible;
        if (_tunAppsPanel.Visible != tunAppsVisible)
            _tunAppsPanel.Visible = tunAppsVisible;
    }

    private void SyncTunAppsListFromState()
    {
        SetTunAppListItems(_state.TunAppProcessPaths ?? new List<string>());
    }

    private void PersistTunAppsFromList()
    {
        _state.TunAppProcessPaths = TunAppPathPolicy.NormalizeDistinctPaths(EnumerateTunAppPaths());
        SaveState();
    }

    private IEnumerable<string> EnumerateTunAppPaths()
    {
        foreach (ListViewItem item in _tunAppsList.Items)
        {
            if (item.Tag is string s && s.Length > 0)
                yield return s;
        }
    }

    private void AddTunAppListItem(string path)
    {
        if (!TunAppPathPolicy.TryNormalizeExePath(path, out var norm))
            return;

        var item = new ListViewItem(Path.GetFileNameWithoutExtension(norm)) { Tag = norm };
        item.SubItems.Add(norm);
        item.ImageIndex = _tunAppIconCache.GetImageIndex(_tunAppIcons, norm);
        _tunAppsList.Items.Add(item);
    }

    private void SetTunAppListItems(IEnumerable<string> paths)
    {
        _tunAppsList.BeginUpdate();
        try
        {
            _tunAppsList.Items.Clear();
            foreach (var p in TunAppPathPolicy.NormalizeDistinctPaths(paths))
                AddTunAppListItem(p);
        }
        finally
        {
            _tunAppsList.EndUpdate();
        }
    }

    private void AddTunAppExecutable()
    {
        using var dialog = new TunAppsPickerDialog(_tunAppsSelectionService, EnumerateTunAppPaths());
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var merged = _tunAppsSelectionService.MergeWithExisting(
            EnumerateTunAppPaths(),
            dialog.SelectedPaths);

        SetTunAppListItems(merged);

        PersistTunAppsFromList();
    }

    private void AddTunAppFromOpenFileDialog()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Выберите исполняемый файл",
            Filter = "Приложения (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK)
            return;

        if (!TunAppPathPolicy.TryNormalizeExePath(ofd.FileName, out var path))
        {
            MessageBox.Show(this, "Укажите существующий файл .exe с полным путём.", "Файл", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var dup = EnumerateTunAppPaths().Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        if (!dup)
            AddTunAppListItem(path);
        PersistTunAppsFromList();
    }

    private void RemoveSelectedTunApp()
    {
        if (_tunAppsList.SelectedItems.Count == 0)
            return;
        _tunAppsList.SelectedItems[0].Remove();
        PersistTunAppsFromList();
    }

    #region Обновления (GitHub Releases)

    private static string BuildUserAgent(string currentSemver) => $"NothingVpn/{currentSemver}";

    private void SyncUpdateBannerCachedInstallerUi()
    {
        if (IsDisposed) return;
        var sem = _updatePendingRelease?.Semver;
        if (string.IsNullOrWhiteSpace(sem))
        {
            _updateBannerInstallCachedBtn.Visible = false;
            _updateBannerDownloadBtn.Visible = true;
            _updateBannerDownloadBtn.Text = AppUpdateUserMessages.ButtonDownloadInstall;
            return;
        }

        var path = TempInstallerCleanup.GetInstallerTempPath(sem);
        var exists = File.Exists(path);
        _updateBannerInstallCachedBtn.Visible = exists;
        _updateBannerDownloadBtn.Visible = !exists;
        _updateBannerDownloadBtn.Text = AppUpdateUserMessages.ButtonDownloadInstall;
    }

    private void OnUpdateBannerInstallCachedClick()
    {
        if (_updatePendingRelease is null)
            return;
        var path = TempInstallerCleanup.GetInstallerTempPath(_updatePendingRelease.Semver);
        if (!File.Exists(path))
        {
            SyncUpdateBannerCachedInstallerUi();
            return;
        }

        OfferInstallDownloadedThenExit(path);
    }

    private void OfferInstallDownloadedThenExit(string installerPath)
    {
        var confirm = MessageBox.Show(
            this,
            AppUpdateUserMessages.ConfirmInstallDownloaded(),
            AppUpdateUserMessages.DialogTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (confirm != DialogResult.Yes)
            return;

        if (!File.Exists(installerPath))
        {
            MessageBox.Show(
                this,
                "Файл обновления не найден. Скачайте установщик снова.",
                AppUpdateUserMessages.DialogTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            SyncUpdateBannerCachedInstallerUi();
            return;
        }

        try
        {
            DisconnectVpnSync();
            InstallerLauncher.ScheduleAfterApplicationExits(installerPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppUpdateUserMessages.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _requestExit();
    }

    private Task UiInvokeAsync(Action action)
    {
        if (IsDisposed)
            return Task.CompletedTask;
        if (!IsHandleCreated)
            return Task.CompletedTask;
        if (!InvokeRequired)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        BeginInvoke(() =>
        {
            try
            {
                if (!IsDisposed)
                    action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private Task<InstallerDownloader.Result> RunInstallerDownloadModalAsync(GitHubReleaseInfo release, string destPath)
    {
        if (IsDisposed || !IsHandleCreated)
            return Task.FromResult(new InstallerDownloader.Result(false, AppUpdateUserMessages.ModalUnavailable));

        var tcs = new TaskCompletionSource<InstallerDownloader.Result>();
        void Run()
        {
            try
            {
                if (IsDisposed)
                {
                    tcs.TrySetResult(new InstallerDownloader.Result(false, AppUpdateUserMessages.ModalWindowClosed));
                    return;
                }

                var r = InstallerDownloadProgressForm.RunModal(
                    this,
                    release.InstallerDownloadUrl,
                    destPath,
                    release.Semver);
                tcs.TrySetResult(r);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(new InstallerDownloader.Result(false, ex.Message));
            }
        }

        if (InvokeRequired)
            BeginInvoke(Run);
        else
            Run();

        return tcs.Task;
    }

    private async Task RunStartupUpdatesAsync()
    {
        try
        {
            TempInstallerCleanup.DeleteOldInstallersInTemp();

            if (!AppVersionInfo.TryGetCurrentSemver(out var currentSemver))
            {
                _appLogger.Warn("app/update", "Не удалось определить версию приложения.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_state.LastRecordedAppSemver))
            {
                _state.LastRecordedAppSemver = currentSemver;
                SaveState();
            }
            else
            {
                var cmp = SemVerComparer.CompareSemver(currentSemver, _state.LastRecordedAppSemver);
                if (cmp > 0)
                {
                    GitHubReleaseInfo? rel = null;
                    try
                    {
                        var ua = BuildUserAgent(currentSemver);
                        using var client = new GitHubReleasesClient(ua);
                        var tag = SemVerComparer.ToProbableGitTag(currentSemver);
                        rel = await client.GetByTagAsync(
                            UpdateChannelOptions.GitHubOwner,
                            UpdateChannelOptions.GitHubRepo,
                            tag,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _appLogger.Warn("app/update", $"Загрузка описания релиза: {ex.Message}");
                    }

                    await UiInvokeAsync(() =>
                    {
                        if (IsDisposed) return;
                        using var f = new ReleaseChangelogForm(currentSemver, rel?.Body ?? "", rel is null);
                        f.ShowDialog(this);
                    }).ConfigureAwait(false);

                    _state.LastRecordedAppSemver = currentSemver;
                    SaveState();
                }
                else if (cmp < 0)
                {
                    _state.LastRecordedAppSemver = currentSemver;
                    SaveState();
                }
            }

            await RefreshUpdateAvailabilityAsync(currentSemver, offerModal: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _appLogger.Error("app/update", ex, "Проверка обновлений при старте не удалась.");
        }
    }

    private async Task OnManualCheckForUpdatesClickAsync()
    {
        if (!AppVersionInfo.TryGetCurrentSemver(out var currentSemver))
        {
            MessageBox.Show(
                this,
                AppUpdateUserMessages.ManualCheckVersionUnknown(),
                AppUpdateUserMessages.DialogTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _updateManualCheckBtn.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            var ok = await RefreshUpdateAvailabilityAsync(currentSemver, offerModal: false).ConfigureAwait(true);
            if (!ok)
            {
                MessageBox.Show(
                    this,
                    AppUpdateUserMessages.ManualCheckNetworkError(),
                    AppUpdateUserMessages.DialogTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_updatePendingRelease is not null &&
                SemVerComparer.CompareSemver(_updatePendingRelease.Semver, currentSemver) > 0)
            {
                MessageBox.Show(
                    this,
                    AppUpdateUserMessages.ManualCheckUpdateAvailable(_updatePendingRelease.Semver),
                    AppUpdateUserMessages.DialogTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    this,
                    AppUpdateUserMessages.ManualCheckUpToDate,
                    AppUpdateUserMessages.DialogTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        finally
        {
            Cursor = Cursors.Default;
            _updateManualCheckBtn.Enabled = true;
        }
    }

    private async Task OnPeriodicUpdateCheckAsync()
    {
        try
        {
            if (!AppVersionInfo.TryGetCurrentSemver(out var currentSemver))
                return;
            if (_state.UpdateLastCheckUtc is { } last &&
                (DateTimeOffset.UtcNow - last).TotalHours < 23.5)
                return;

            await RefreshUpdateAvailabilityAsync(currentSemver, offerModal: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _appLogger.Warn("app/update", $"Периодическая проверка: {ex.Message}");
        }
    }

    private async Task<bool> RefreshUpdateAvailabilityAsync(string currentSemver, bool offerModal)
    {
        GitHubReleaseInfo? latest = null;
        try
        {
            var ua = BuildUserAgent(currentSemver);
            using var client = new GitHubReleasesClient(ua);
            latest = await client.GetLatestAsync(
                UpdateChannelOptions.GitHubOwner,
                UpdateChannelOptions.GitHubRepo,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _appLogger.Warn("app/update", $"GitHub releases: {ex.Message}");
            return false;
        }

        _state.UpdateLastCheckUtc = DateTimeOffset.UtcNow;
        SaveState();

        await UiInvokeAsync(() =>
        {
            if (IsDisposed) return;
            if (latest is null || SemVerComparer.CompareSemver(latest.Semver, currentSemver) <= 0)
            {
                _updatePendingRelease = null;
                _updateBannerPanel.Visible = false;
                SyncUpdateBannerCachedInstallerUi();
                return;
            }

            _updatePendingRelease = latest;
            _updateBannerLabel.Text = AppUpdateUserMessages.BannerLine(latest.Semver);
            _updateBannerPanel.Visible = true;
            SyncUpdateBannerCachedInstallerUi();

            if (!offerModal)
                return;
            if (string.Equals(_state.UpdateDismissedModalForTag, latest.TagName, StringComparison.OrdinalIgnoreCase))
                return;

            var r = MessageBox.Show(
                this,
                AppUpdateUserMessages.OfferDownloadOnStartup(latest.Semver),
                AppUpdateUserMessages.DialogTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
                _ = StartDownloadAndRunInstallerAsync(latest);
            else
            {
                _state.UpdateDismissedModalForTag = latest.TagName;
                SaveState();
            }
        }).ConfigureAwait(false);

        return true;
    }

    private async Task OnUpdateBannerDownloadClickAsync()
    {
        if (_updatePendingRelease is null || _updateDownloadBusy)
            return;
        await StartDownloadAndRunInstallerAsync(_updatePendingRelease).ConfigureAwait(false);
    }

    private async Task StartDownloadAndRunInstallerAsync(GitHubReleaseInfo release)
    {
        if (_updateDownloadBusy)
            return;
        _updateDownloadBusy = true;
        try
        {
            await UiInvokeAsync(() =>
            {
                _updateBannerDownloadBtn.Enabled = false;
                _updateBannerInstallCachedBtn.Enabled = false;
            }).ConfigureAwait(false);

            var path = TempInstallerCleanup.GetInstallerTempPath(release.Semver);
            var result = await RunInstallerDownloadModalAsync(release, path).ConfigureAwait(false);

            await UiInvokeAsync(() =>
            {
                _updateBannerDownloadBtn.Enabled = true;
                _updateBannerInstallCachedBtn.Enabled = true;
                if (!result.Ok)
                {
                    MessageBox.Show(
                        this,
                        result.Error ?? AppUpdateUserMessages.DownloadFailedFallback,
                        AppUpdateUserMessages.DialogTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                SyncUpdateBannerCachedInstallerUi();
                OfferInstallDownloadedThenExit(path);
            }).ConfigureAwait(false);
        }
        finally
        {
            _updateDownloadBusy = false;
        }
    }

    #endregion
}


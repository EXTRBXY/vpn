using System.ComponentModel;
using System.Diagnostics;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Domain.Updates;
using NothingVpn.Infrastructure.Profile;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Security;
using NothingVpn.Infrastructure.Store;
using NothingVpn.Infrastructure.WinInet;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.TunApps;
using NothingVpn.Presentation;
using NothingVpn.Tray.Internal.Diagnostics;
using NothingVpn.Tray.Internal.Windows;
using NothingVpn.Tray.Internal.Updates;
using NothingVpn.Tray.Internal.UI;

namespace NothingVpn.Tray;

internal sealed class MainForm : Form
{
    private readonly AppPaths _paths;
    private readonly IProfileService _profileService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IConnectionScreenController _connectionScreenController;
    private readonly IConnectionSettingsController _connectionSettingsController;
    private readonly ITunAppsController _tunAppsController;
    private readonly IRuleSetManagementController _ruleSetManagementController;
    private readonly IRuleSetFileService _ruleSetFileService;
    private readonly IAppUpdateController _appUpdateController;
    private readonly IInstallerUpdateService _installerUpdateService;
    private readonly IInstallerLaunchService _installerLaunchService;
    private readonly IConnectionController _connectionController;
    private readonly IConnectionDiagnosticController _connectionDiagnosticController;
    private readonly InMemoryLogStore _logStore;
    private readonly AppLogger _appLogger;
    private readonly System.Action _requestExit;
    private readonly System.Action<bool>? _vpnConnectionStateChanged;
    private readonly TunAppsSelectionService _tunAppsSelectionService;

    private AppStateModel _state = new();
    private IReadOnlyList<VpnProfile> _profiles = Array.Empty<VpnProfile>();

    private readonly TabControl _tabs;
    private readonly LogPanelControl _logPanel;
    private readonly ConnectionSettingsUi _connectionSettings;

    private readonly ConnectionControlPanelControl _connectionPanel;
    private readonly ConnectionStatusPanelControl _statusPanel;

    private readonly TunAppsPanelControl _tunAppsPanel;

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
    private AppReleaseModel? _updatePendingRelease;
    private bool _updateDownloadBusy;

    private bool _connectionUiUpdateQueued;
    private bool _loadingData;

    public MainForm(
        AppPaths paths,
        IProfileService profileService,
        ISubscriptionService subscriptionService,
        IConnectionScreenController connectionScreenController,
        IConnectionSettingsController connectionSettingsController,
        ITunAppsController tunAppsController,
        IRuleSetManagementController ruleSetManagementController,
        IRuleSetFileService ruleSetFileService,
        IAppUpdateController appUpdateController,
        IInstallerUpdateService installerUpdateService,
        IInstallerLaunchService installerLaunchService,
        IConnectionController connectionController,
        IConnectionDiagnosticController connectionDiagnosticController,
        InMemoryLogStore logStore,
        System.Action? requestExit = null,
        System.Action<bool>? vpnConnectionStateChanged = null)
    {
        _paths = paths;
        _profileService = profileService;
        _subscriptionService = subscriptionService;
        _connectionScreenController = connectionScreenController;
        _connectionSettingsController = connectionSettingsController;
        _tunAppsController = tunAppsController;
        _ruleSetManagementController = ruleSetManagementController;
        _ruleSetFileService = ruleSetFileService;
        _appUpdateController = appUpdateController;
        _installerUpdateService = installerUpdateService;
        _installerLaunchService = installerLaunchService;
        _connectionController = connectionController;
        _connectionDiagnosticController = connectionDiagnosticController;
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
        Width = 680;
        Height = 820;
        MinimumSize = new Size(560, 640);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        SuspendLayout();

        _tabs = new TabControl { Dock = DockStyle.Fill };
        _connectionSettings = ConnectionSettingsPanelBuilder.Build();
        var tabMain = _connectionSettings.Tab;
        tabMain.Text = "Основное";
        var tabLogs = new TabPage("Логи");
        _tabs.TabPages.Add(tabMain);
        _tabs.TabPages.Add(tabLogs);
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
        var connectionSettingsRoot = _connectionSettings.Tab.Controls.Cast<Control>().Single();
        _connectionSettings.Tab.Controls.Remove(connectionSettingsRoot);

        _connectionPanel = new ConnectionControlPanelControl();

        var mainStack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(UiMetrics.Space12, UiMetrics.Space12, UiMetrics.Space12, UiMetrics.Space8)
        };
        mainStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        connectionGroup.Controls.Add(_connectionPanel);

        _tunAppsPanel = new TunAppsPanelControl();

        _statusPanel = new ConnectionStatusPanelControl();

        var statusGroup = new GroupBox
        {
            Text = "Текущее состояние",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        statusGroup.Controls.Add(_statusPanel);
        mainStack.Controls.Add(statusGroup, 0, 0);
        mainStack.Controls.Add(connectionGroup, 0, 1);
        _connectionSettings.TunAppsHost.Controls.Add(_tunAppsPanel);

        _logPanel = new LogPanelControl(_logStore, _appLogger);
        tabLogs.Controls.Add(_logPanel);

        var ruleSetsPanel = new RuleSetsPanelControl();
        _builtinRuleSetsGrid = ruleSetsPanel.BuiltinGrid;
        _userRuleSetsGrid = ruleSetsPanel.UserGrid;
        _builtinRuleSetsFetchOrRemoveBtn = ruleSetsPanel.FetchOrRemoveButton;
        _builtinRuleSetsCheckUpdatesBtn = ruleSetsPanel.CheckUpdatesButton;
        _builtinRuleSetsOtherListsBtn = ruleSetsPanel.OtherListsButton;
        _userRuleSetsAddBtn = ruleSetsPanel.AddUserButton;
        _userRuleSetsRemoveBtn = ruleSetsPanel.RemoveUserButton;

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

        mainStack.Controls.Add(connectionSettingsRoot, 0, 2);
        mainStack.Controls.Add(ruleSetsPanel, 0, 3);
        mainStack.Controls.Add(_updateBannerPanel, 0, 4);
        tabMain.Controls.Add(updateManualCheckPanel);
        tabMain.Controls.Add(mainStack);

        _updateBannerInstallCachedBtn.Click += (_, _) => OnUpdateBannerInstallCachedClick();
        _updateBannerDownloadBtn.Click += async (_, _) => await OnUpdateBannerDownloadClickAsync();

        _dnsDebounceTimer = new System.Windows.Forms.Timer { Interval = 350 };
        _dnsDebounceTimer.Tick += (_, _) =>
        {
            _dnsDebounceTimer.Stop();
            if (!_dnsUiReady) return;
            SaveConnectionSettingsFromUi(showDialogs: false);
        };

        _connectionPanel.ProfilesRequested += (_, _) => OpenProfilesDialog();
        _connectionPanel.StartRequested += async (_, _) => await StartAsync();
        _connectionPanel.StopRequested += (_, _) => _ = StopAsync();
        _connectionPanel.ProfileChanged += (_, _) =>
        {
            if (_loadingData) return;
            if (_connectionPanel.SelectedProfile is VpnProfile p)
            {
                _connectionScreenController.SelectProfile(_state, p.Id);
                UpdateButtons();
            }
        };
        _connectionPanel.PortChanged += (_, _) =>
        {
            if (_loadingData) return;
            _state.LocalMixedPort = _connectionPanel.Port;
            SaveState();
        };
        _connectionPanel.ModeChanged += (_, _) =>
        {
            if (_loadingData) return;
            _state.Mode = ComboIndexToMode(_connectionPanel.ModeIndex);
            SaveState();
            UpdateTitle();
            UpdateConnectionTabVisibility();
            UpdateButtons();
            if (_dnsUiReady)
                UpdateDnsDetourControl();
        };
        _tunAppsPanel.AddRequested += (_, _) => AddTunAppExecutable();
        _tunAppsPanel.BrowseRequested += (_, _) => AddTunAppFromOpenFileDialog();
        _tunAppsPanel.RemoveRequested += (_, _) => RemoveSelectedTunApp();
        _logPanel.DebugLogsChanged += (_, _) =>
        {
            if (_loadingData) return;
            _state.DebugLogs = _logPanel.DebugLogs;
            // Keep sing-box logging minimal by default.
            _state.SingBoxLogLevel = _state.DebugLogs ? "debug" : "warn";
            SaveState();
        };
        _connectionPanel.PingRequested += async (_, _) => await PingAsync();
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
            UpdateDnsDetourControl();
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

        _connectionController.ConnectionStateChanged += (_, connected) =>
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
            _logPanel.Stop();
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
                _connectionPanel.SelectProfile(match);
        }

        _connectionPanel.ModeIndex = ModeToComboIndex(_state.Mode);
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
            if (_connectionController.IsRunning) return;
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
            if (!_connectionController.IsRunning) return;
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
            _logPanel.Stop();
            NotifyVpnConnectionState(false);
        }
        catch
        {
            // best-effort
        }
    }

    private void DisconnectVpnSync()
    {
        _connectionController.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
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
        _loadingData = true;
        try
        {
            LoadDataCore();
        }
        finally
        {
            _loadingData = false;
        }
    }

    private void LoadDataCore()
    {
        var snapshot = _connectionScreenController.Load();
        _profiles = snapshot.Profiles;
        _state = snapshot.State;

        _connectionPanel.LoadProfiles(_profiles, snapshot.SelectedProfile);
        _connectionPanel.SetPort(_state.LocalMixedPort);
        _connectionPanel.ModeIndex = ModeToComboIndex(_state.Mode);
        SyncTunAppsListFromState();
        SyncRuleSetsGridFromState();
        UpdateConnectionTabVisibility();
        _logPanel.DebugLogs = _state.DebugLogs;
        _connectionSettings.LoadFromState(_state);
        UpdateDnsDetourControl();
        _dnsUiReady = true;
        UpdateTitle();
    }

    private void SaveState()
    {
        if (_loadingData) return;
        _connectionScreenController.Save(_state);
    }

    private void RestartConnectionSettingsDebounce()
    {
        if (_loadingData) return;
        _dnsDebounceTimer.Stop();
        _dnsDebounceTimer.Start();
    }

    private void UpdateDnsDetourControl()
    {
        var allowProxyDetour = DnsDetourPolicy.AllowsProxyDetour(_state.Mode);
        if (!allowProxyDetour)
        {
            _dnsDetourCombo.SelectedIndex = DnsPolicy.DetourToComboIndex("direct");
            _state.DnsDetour = "direct";
        }

        var isDoh = _dnsModeCombo.SelectedIndex == 1;
        _dnsDetourCombo.Enabled = isDoh && allowProxyDetour && !_connectionController.IsRunning;
        if (TunAppsPolicy.IsTunApps(_state.Mode))
        {
            _connectionSettings.DnsNotice.Text =
                "TUN (приложения): DNS перехватывается sing-box (DoH/system); в proxy уходит только трафик выбранных .exe. DoH detour через proxy недоступен.";
            _connectionSettings.DnsNotice.Visible = true;
        }
        else
        {
            _connectionSettings.DnsNotice.Text = !allowProxyDetour && isDoh
                ? "В этом режиме DoH идёт напрямую."
                : "";
            _connectionSettings.DnsNotice.Visible = !string.IsNullOrEmpty(_connectionSettings.DnsNotice.Text);
        }
    }
    private void ApplyDnsPresetToBoxes()
    {
        try
        {
            var idx = _dnsPresetCombo.SelectedIndex;
            if (idx < 0 || idx >= 4) return;

            var dns = new DnsSettings
            {
                Mode = "doh",
                DohServer = _dohServerBox.Text,
                DohPath = _dohPathBox.Text,
                DohSni = _dohSniBox.Text,
                Detour = DnsPolicy.ComboIndexToDetour(_dnsDetourCombo.SelectedIndex)
            };
            DnsPolicy.ApplyPreset(idx, dns);
            _dohServerBox.Text = dns.DohServer;
            _dohPathBox.Text = dns.DohPath;
            _dohSniBox.Text = dns.DohSni;
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
            var dnsMode = _dnsModeCombo.SelectedIndex == 0 ? "system" : "doh";
            var detour = DnsPolicy.ComboIndexToDetour(_dnsDetourCombo.SelectedIndex);
            var draft = new ConnectionSettingsDraft(
                new ProxyConnectionSettings { ProxyOverride = _proxyOverrideBox.Text },
                new TunSettings
                {
                    InterfaceName = _tunInterfaceNameBox.Text,
                    AddressCidr = _tunAddressCidrModeCombo.SelectedIndex == 0
                        ? "auto"
                        : (_tunAddressCidrBox.Text ?? "").Trim(),
                    Mtu = (int)_tunMtu.Value,
                    Stack = TunSettingsPolicy.ComboIndexToStack(_tunStackCombo.SelectedIndex),
                    AutoRoute = _tunAutoRoute.Checked,
                    StrictRoute = _tunStrictRoute.Checked
                },
                new DnsSettings
                {
                    Mode = dnsMode,
                    DohServer = _dohServerBox.Text,
                    DohPath = _dohPathBox.Text,
                    DohSni = _dohSniBox.Text,
                    Detour = detour
                });

            _connectionSettingsController.Save(_state, draft);

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

    private void SyncRuleSetsGridFromState()
    {
        var snapshot = _ruleSetManagementController.Load(_state);
        _builtinRuleSetsBinding = new BindingList<UserRuleSetModel>(snapshot.Builtin.ToList());
        _userRuleSetsBinding = new BindingList<UserRuleSetModel>(snapshot.User.ToList());
        _builtinRuleSetsGrid.DataSource = _builtinRuleSetsBinding;
        _userRuleSetsGrid.DataSource = _userRuleSetsBinding;
        RefreshBuiltinGridRowStyles();
        UpdateBuiltinFetchOrRemoveButton();
    }

    private void SaveRuleSetsFromGrid()
    {
        if (_loadingData) return;
        try
        {
            _ruleSetManagementController.Save(_state, _builtinRuleSetsBinding, _userRuleSetsBinding);
        }
        catch
        {
            // best-effort
        }
    }

    private bool RuleSetFileExists(UserRuleSetModel rs)
    {
        return _ruleSetFileService.Exists(rs);
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

            var imported = _ruleSetFileService.Import(ofd.FileName);
            _userRuleSetsBinding.Add(_ruleSetManagementController.CreateUserRuleSet(imported.Name, imported.FileName));
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
                try
                {
                    _ruleSetFileService.Delete(rs);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Удалить файл не удалось", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

            }

            _ruleSetManagementController.MarkBuiltinFilesRemoved(
                _state,
                _builtinRuleSetsBinding,
                _userRuleSetsBinding,
                targets);
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

        var result = await _ruleSetFileService.DownloadBuiltinAsync(rs, useConditionalRequest: false, CancellationToken.None)
            .ConfigureAwait(true);
        if (!result.Success)
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

    private void OpenOtherRuleSetLists()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _ruleSetFileService.CatalogUrl,
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

            var anyFile = targets.Any(_ruleSetFileService.Exists);
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
                if (!_ruleSetFileService.Exists(rs))
                    continue;

                var useConditional = !string.IsNullOrWhiteSpace(rs.RemoteEtag);
                var result = await _ruleSetFileService.DownloadBuiltinAsync(
                    rs,
                    useConditional,
                    CancellationToken.None).ConfigureAwait(true);

                if (!result.Success)
                {
                    failed.Add($"{rs.Name}: {result.Error}");
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
        _ruleSetManagementController.MarkDownloaded(_state, rs, newEtag);
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

    private async Task PingAsync()
    {
        var result = await _connectionDiagnosticController.RunAsync(_state.Mode, _connectionController.IsRunning);
        if (!string.IsNullOrWhiteSpace(result.LogMessage))
            _appLogger.Warn("app/smoke", result.LogMessage);
        var icon = result.Status == ConnectionDiagnosticStatus.Failure
            ? MessageBoxIcon.Warning
            : MessageBoxIcon.Information;
        MessageBox.Show(this, result.Message, "Пинг", MessageBoxButtons.OK, icon);
    }

    private void UpdateTitle()
    {
        Text = CreateConnectionViewState().WindowTitle;
    }

    private void UpdateButtons()
    {
        var viewState = CreateConnectionViewState();
        _connectionPanel.ApplyAvailability(viewState.CanStart, viewState.CanStop, viewState.CanEditConnection);
        _tunAppsPanel.SetEditingEnabled(viewState.CanEditTunApps);

        _statusPanel.Apply(viewState);
        _connectionSettings.SetConnectionFieldsEnabled(viewState.CanEditConnection);
        UpdateDnsDetourControl();
    }

    private ConnectionViewState CreateConnectionViewState() => ConnectionViewStateFactory.Create(
        _state,
        _connectionPanel.SelectedProfile,
        _connectionController.IsRunning,
        _connectionController.IsAdministrator);

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
        if (_connectionPanel.SelectedProfile is not VpnProfile p) return;

        try
        {
            UpdateButtons();
            var result = await _connectionController.StartAsync(p.Id, _state.Mode);
            if (result.ExitCurrentProcess)
            {
                BeginInvoke(_requestExit);
                return;
            }

            if (result.Connected)
            {
                _logPanel.Start();
                NotifyVpnConnectionState(true);
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error("app/runtime", ex, "Запуск VPN завершился ошибкой.");
            MessageBox.Show(this, ex.Message, "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ApplyStoppedUi();
        }
        finally
        {
            UpdateButtons();
        }
    }

    private void NotifyVpnConnectionState(bool connected)
    {
        try { _vpnConnectionStateChanged?.Invoke(connected); } catch { }
    }

    private async Task StopAsync()
    {
        _connectionPanel.DisableStartStop();
        try
        {
            await _connectionController.StopAsync().ConfigureAwait(false);
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
            ApplyStoppedUi();
            if (!IsDisposed && IsHandleCreated)
            {
                void UiFinish()
                {
                    _logPanel.RefreshNow();
                    UpdateButtons();
                }

                if (InvokeRequired)
                    BeginInvoke(UiFinish);
                else
                    UiFinish();
            }
        }
    }

    private void ApplyStoppedUi()
    {
        _logPanel.Stop();
        NotifyVpnConnectionState(false);
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
        _tunAppsPanel.SetModeVisible(tunAppsVisible);
    }

    private void SyncTunAppsListFromState()
    {
        SetTunAppListItems(_state.TunAppProcessPaths ?? new List<string>());
    }

    private IEnumerable<string> EnumerateTunAppPaths()
    {
        return _tunAppsPanel.Paths;
    }

    private void SetTunAppListItems(IEnumerable<string> paths)
    {
        _tunAppsPanel.SetPaths(_tunAppsController.Normalize(paths));
    }

    private void AddTunAppExecutable()
    {
        using var dialog = new TunAppsPickerDialog(_tunAppsSelectionService, EnumerateTunAppPaths());
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var merged = _tunAppsController.AddAndSave(
            _state,
            EnumerateTunAppPaths(),
            dialog.SelectedPaths);

        SetTunAppListItems(merged);
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

        if (!_tunAppsController.TryNormalize(ofd.FileName, out var path))
        {
            MessageBox.Show(this, "Укажите существующий файл .exe с полным путём.", "Файл", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var paths = _tunAppsController.AddAndSave(_state, EnumerateTunAppPaths(), new[] { path });
        SetTunAppListItems(paths);
    }

    private void RemoveSelectedTunApp()
    {
        if (_tunAppsPanel.SelectedPath is not string removedPath)
            return;
        var paths = _tunAppsController.RemoveAndSave(_state, EnumerateTunAppPaths(), removedPath);
        SetTunAppListItems(paths);
    }

    #region Обновления (GitHub Releases)

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

        var exists = _installerUpdateService.IsCached(sem);
        _updateBannerInstallCachedBtn.Visible = exists;
        _updateBannerDownloadBtn.Visible = !exists;
        _updateBannerDownloadBtn.Text = AppUpdateUserMessages.ButtonDownloadInstall;
    }

    private void OnUpdateBannerInstallCachedClick()
    {
        if (_updatePendingRelease is null)
            return;
        var path = _installerUpdateService.GetCachedInstallerPath(_updatePendingRelease.Semver);
        if (!_installerUpdateService.IsCached(_updatePendingRelease.Semver))
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
            _installerLaunchService.ScheduleAfterApplicationExits(installerPath);
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

    private Task<InstallerDownloadResult> RunInstallerDownloadModalAsync(AppReleaseModel release)
    {
        if (IsDisposed || !IsHandleCreated)
            return Task.FromResult(new InstallerDownloadResult(false, AppUpdateUserMessages.ModalUnavailable));

        var tcs = new TaskCompletionSource<InstallerDownloadResult>();
        void Run()
        {
            try
            {
                if (IsDisposed)
                {
                    tcs.TrySetResult(new InstallerDownloadResult(false, AppUpdateUserMessages.ModalWindowClosed));
                    return;
                }

                var r = InstallerDownloadProgressForm.RunModal(
                    this,
                    _installerUpdateService,
                    release);
                tcs.TrySetResult(r);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(new InstallerDownloadResult(false, ex.Message));
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
            _installerUpdateService.CleanupOldInstallers();

            if (!AppVersionInfo.TryGetCurrentSemver(out var currentSemver))
            {
                _appLogger.Warn("app/update", "Не удалось определить версию приложения.");
                return;
            }

            var versionTransition = _appUpdateController.RecordInstalledVersion(_state, currentSemver);
            if (versionTransition == InstalledVersionTransition.Upgraded)
            {
                AppReleaseModel? rel = null;
                try
                {
                    rel = await _appUpdateController.GetCurrentReleaseAsync(currentSemver, CancellationToken.None)
                        .ConfigureAwait(false);
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
                SemanticVersionPolicy.Compare(_updatePendingRelease.Semver, currentSemver) > 0)
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
            if (!_appUpdateController.IsPeriodicCheckDue(_state, DateTimeOffset.UtcNow))
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
        var check = await _appUpdateController.CheckAsync(_state, currentSemver, CancellationToken.None)
            .ConfigureAwait(false);
        if (!check.Succeeded)
        {
            _appLogger.Warn("app/update", $"GitHub releases: {check.Error}");
            return false;
        }
        var latest = check.AvailableRelease;

        await UiInvokeAsync(() =>
        {
            if (IsDisposed) return;
            if (latest is null)
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
            if (!_appUpdateController.ShouldOffer(_state, latest))
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
                _appUpdateController.DismissOffer(_state, latest);
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

    private async Task StartDownloadAndRunInstallerAsync(AppReleaseModel release)
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

            var result = await RunInstallerDownloadModalAsync(release).ConfigureAwait(false);

            await UiInvokeAsync(() =>
            {
                _updateBannerDownloadBtn.Enabled = true;
                _updateBannerInstallCachedBtn.Enabled = true;
                if (!result.Success)
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
                OfferInstallDownloadedThenExit(result.InstallerPath!);
            }).ConfigureAwait(false);
        }
        finally
        {
            _updateDownloadBusy = false;
        }
    }

    #endregion
}


using NothingVpn.Tray.Internal.Profile;
using NothingVpn.Tray.Internal.SingBox;
using NothingVpn.Tray.Internal.Security;
using NothingVpn.Tray.Internal.Store;
using NothingVpn.Tray.Internal.WinInet;
using NothingVpn.Tray.Internal.Diagnostics;
using NothingVpn.Tray.Internal.Windows;

namespace NothingVpn.Tray;

internal sealed class MainForm : Form
{
    private readonly AppPaths _paths;
    private readonly JsonProfileStore _profileStore;
    private readonly JsonStateStore _stateStore;
    private readonly SingBoxRunner _runner;
    private readonly WinInetProxyController _proxy;
    private readonly InMemoryLogStore _logStore;
    private readonly Action _requestExit;
    private readonly Action<bool>? _vpnConnectionStateChanged;

    private AppState _state = new();
    private IReadOnlyList<VlessProfile> _profiles = Array.Empty<VlessProfile>();

    private readonly TabControl _tabs;
    private readonly TabPage _tabLogs;

    private readonly ComboBox _profilesCombo;
    private readonly Button _importBtn;
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

    private readonly Panel _tunAppsPanel;
    private readonly ListBox _tunAppsList;
    private readonly Button _tunAppsAddBtn;
    private readonly Button _tunAppsRemoveBtn;

    public MainForm(AppPaths paths, JsonProfileStore profileStore, JsonStateStore stateStore, SingBoxRunner runner, WinInetProxyController proxy, InMemoryLogStore logStore, Action? requestExit = null, Action<bool>? vpnConnectionStateChanged = null)
    {
        _paths = paths;
        _profileStore = profileStore;
        _stateStore = stateStore;
        _runner = runner;
        _proxy = proxy;
        _logStore = logStore;
        _requestExit = requestExit ?? (() => Application.Exit());
        _vpnConnectionStateChanged = vpnConnectionStateChanged;

        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Text = "Nothing VPN (прокси)";
        Width = 640;
        Height = 480;
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterScreen;

        _tabs = new TabControl { Dock = DockStyle.Fill };
        var tabMain = new TabPage("Основное");
        _tabLogs = new TabPage("Логи");
        var tabAdvanced = new TabPage("Дополнительно");
        _tabs.TabPages.Add(tabMain);
        _tabs.TabPages.Add(_tabLogs);
        _tabs.TabPages.Add(tabAdvanced);
        Controls.Add(_tabs);

        // Main tab: no SplitContainer -> no forced empty space
        tabMain.AutoScroll = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 5,
            RowCount = 4,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "Профиль", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _profilesCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            MinimumSize = new Size(220, 0)
        };
        layout.Controls.Add(_profilesCombo, 1, 0);
        _importBtn = new Button { Text = "Импорт", Anchor = AnchorStyles.Right, AutoSize = true };
        layout.Controls.Add(_importBtn, 2, 0);
        // no logs folder button
        _pingBtn = new Button { Text = "Пинг", Anchor = AnchorStyles.Right, AutoSize = true };
        layout.Controls.Add(_pingBtn, 3, 0);
        layout.Controls.Add(new Label { Text = "", AutoSize = true }, 4, 0);

        layout.Controls.Add(new Label { Text = "Режим", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _modeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            MinimumSize = new Size(220, 0)
        };
        _modeCombo.Items.AddRange(new object[] { "Прокси", "TUN (весь трафик)", "TUN (выбранные приложения)" });
        layout.Controls.Add(_modeCombo, 1, 1);
        _startBtn = new Button { Text = "Старт", Anchor = AnchorStyles.Right, AutoSize = true };
        layout.Controls.Add(_startBtn, 2, 1);
        _stopBtn = new Button { Text = "Стоп", Anchor = AnchorStyles.Right, AutoSize = true };
        layout.Controls.Add(_stopBtn, 3, 1);
        layout.Controls.Add(new Label { Text = "", AutoSize = true }, 4, 1);

        layout.Controls.Add(new Label { Text = "Локальный порт", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        layout.Controls.Add(_port, 1, 2);
        layout.Controls.Add(new Label { Text = "Логи", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        _debugLogs = new CheckBox { Text = "Debug (без редактирования)", AutoSize = true, Anchor = AnchorStyles.Left };
        layout.Controls.Add(_debugLogs, 1, 3);
        // (Trust moved to Advanced tab)
        _trustSingBoxBtn = new Button { Text = "Доверять sing-box.exe", Anchor = AnchorStyles.Left };
        _singBoxHashLabel = new Label { Text = "", AutoSize = true, Anchor = AnchorStyles.Left };

        tabMain.Controls.Add(layout);

        _tunAppsPanel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 0, 12, 8),
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
        tunAppsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        tunAppsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tunAppsRoot.Controls.Add(new Label
        {
            Text = "Исполняемые файлы, чей трафик идёт через VPN (полный путь .exe). Дочерние процессы нужно добавлять отдельно.",
            AutoSize = true,
            MaximumSize = new Size(560, 0)
        }, 0, 0);
        _tunAppsList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        tunAppsRoot.Controls.Add(_tunAppsList, 0, 1);
        var tunAppsBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _tunAppsAddBtn = new Button { Text = "Добавить…", AutoSize = true };
        _tunAppsRemoveBtn = new Button { Text = "Удалить", AutoSize = true };
        tunAppsBtns.Controls.Add(_tunAppsAddBtn);
        tunAppsBtns.Controls.Add(_tunAppsRemoveBtn);
        tunAppsRoot.Controls.Add(tunAppsBtns, 0, 2);
        _tunAppsPanel.Controls.Add(tunAppsRoot);
        tabMain.Controls.Add(_tunAppsPanel);

        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12),
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _statusValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _adminValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _modeValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _profileValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _portValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };

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
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        tabMain.Controls.Add(statusLayout);

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
            Padding = new Padding(8),
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        logsRoot.Controls.Add(logsTop, 0, 0);

        logsTop.Controls.Add(new Label { Text = "Уровень", AutoSize = true, Margin = new Padding(6, 8, 6, 0) });
        _logFilterCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _logFilterCombo.Items.AddRange(new object[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR" });
        logsTop.Controls.Add(_logFilterCombo);

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
            Padding = new Padding(12),
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
        tabAdvanced.Controls.Add(advLayout);

        _logTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _logTimer.Tick += (_, _) => RefreshLog();

        _importBtn.Click += (_, _) => ImportFromClipboard();
        _startBtn.Click += async (_, _) => await StartAsync();
        _stopBtn.Click += (_, _) => Stop();
        _profilesCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_profilesCombo.SelectedItem is VlessProfile p)
            {
                _state.ActiveProfileId = p.Id;
                _stateStore.Save(_state);
                UpdateButtons();
            }
        };
        _port.ValueChanged += (_, _) =>
        {
            _state.LocalMixedPort = (int)_port.Value;
            _stateStore.Save(_state);
        };
        _modeCombo.SelectedIndexChanged += (_, _) =>
        {
            _state.Mode = ComboIndexToMode(_modeCombo.SelectedIndex);
            _stateStore.Save(_state);
            UpdateTitle();
            UpdateTunAppsPanelVisibility();
            UpdateButtons();
        };
        _tunAppsAddBtn.Click += (_, _) => AddTunAppExecutable();
        _tunAppsRemoveBtn.Click += (_, _) => RemoveSelectedTunApp();
        _debugLogs.CheckedChanged += (_, _) =>
        {
            _state.DebugLogs = _debugLogs.Checked;
            // Keep sing-box logging minimal by default.
            _state.SingBoxLogLevel = _state.DebugLogs ? "debug" : "warn";
            _stateStore.Save(_state);
        };
        _trustSingBoxBtn.Click += (_, _) => TrustCurrentSingBox();
        _copyLogsBtn.Click += (_, _) => CopyLogsToClipboard();
        _downloadLogsBtn.Click += (_, _) => DownloadLogs();
        _pingBtn.Click += async (_, _) => await PingAsync();
        _logFilterCombo.SelectedIndexChanged += (_, _) => RefreshLog();

        _runner.ProcessExited += (_, _) =>
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(() =>
                {
                    UpdateButtons();
                    NotifyVpnConnectionState(false);
                });
            }
            catch
            {
                // ignore
            }
        };

        LoadData();
        UpdateButtons();

        FormClosing += (_, e) =>
        {
            // MainForm may be hidden-to-tray by outer controller; default close is allowed.
            _logTimer.Stop();
        };
    }

    public void ApplyStartup(StartupArgs? startup)
    {
        if (startup is null) return;

        if (!string.IsNullOrWhiteSpace(startup.Mode))
        {
            _state.Mode = SingBoxConfigGenerator.NormalizeMode(startup.Mode);
            _stateStore.Save(_state);
        }

        if (!string.IsNullOrWhiteSpace(startup.ProfileId))
        {
            var match = _profiles.FirstOrDefault(p => string.Equals(p.Id, startup.ProfileId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                _profilesCombo.SelectedItem = match;
        }

        _modeCombo.SelectedIndex = ModeToComboIndex(_state.Mode);
        SyncTunAppsListFromState();
        UpdateTunAppsPanelVisibility();
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
            if (_runner.IsRunning) return;
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
            if (!_runner.IsRunning) return;
            Stop();
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
            // Idempotent cleanup.
            Stop();
        }
        catch
        {
            // best-effort
        }
    }

    private void LoadData()
    {
        _profiles = _profileStore.Load();
        _state = _stateStore.Load();

        _profilesCombo.DataSource = _profiles.ToList();
        _profilesCombo.DisplayMember = nameof(VlessProfile.Name);

        var active = _profiles.FirstOrDefault(p => p.Id == _state.ActiveProfileId) ?? _profiles.FirstOrDefault();
        if (active is not null)
        {
            _profilesCombo.SelectedItem = active;
            _state.ActiveProfileId = active.Id;
            _stateStore.Save(_state);
        }

        _port.Value = Math.Clamp(_state.LocalMixedPort, 1, 65535);
        if (_state.TunAppProcessPaths is null)
            _state.TunAppProcessPaths = new List<string>();
        _modeCombo.SelectedIndex = ModeToComboIndex(_state.Mode);
        SyncTunAppsListFromState();
        UpdateTunAppsPanelVisibility();
        _debugLogs.Checked = _state.DebugLogs;
        if (_logFilterCombo.SelectedIndex < 0) _logFilterCombo.SelectedIndex = 2; // INFO
        UpdateSingBoxHashLabel();
        UpdateTitle();
    }

    private void CopyLogsToClipboard()
    {
        try
        {
            var text = _logBox.Text ?? "";
            if (text.Length == 0) return;
            Clipboard.SetText(text, TextDataFormat.Text);
        }
        catch (Exception ex)
        {
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
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Скачать логи не удалось", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task PingAsync()
    {
        if (!_runner.IsRunning)
        {
            MessageBox.Show(this, "Сначала нажмите «Старт».", "Пинг", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var isTun = AppState.IsTunMode(_state.Mode);
        var isTunApps = string.Equals(_state.Mode, "tun_apps", StringComparison.OrdinalIgnoreCase);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!isTun)
            {
                // "Ping" for proxy mode: verify the local proxy can CONNECT to a well-known host.
                var r = await ProxySmokeTest.HttpConnectAsync(
                    proxyHost: "127.0.0.1",
                    proxyPort: _state.LocalMixedPort,
                    targetHost: "1.1.1.1",
                    targetPort: 443,
                    timeout: TimeSpan.FromSeconds(3));

                sw.Stop();
                if (!r.Success)
                    throw new InvalidOperationException(r.Error ?? "Proxy test failed.");

                MessageBox.Show(this, $"Прокси: OK\nВремя: {sw.ElapsedMilliseconds} мс", "Пинг", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (isTunApps)
            {
                // Трафик этого процесса обычно не в списке — ipify проверяет «direct», не VLESS.
                var r = await TunSmokeTest.IpifyAsync(TimeSpan.FromSeconds(4));
                sw.Stop();
                if (!r.Success)
                    throw new InvalidOperationException(r.Error ?? "TUN test failed.");

                MessageBox.Show(this,
                    $"Связность: OK\nВремя: {sw.ElapsedMilliseconds} мс\n\nВ режиме «TUN (выбранные приложения)» этот тест идёт из процесса приложения и может не совпадать с маршрутом выбранных .exe.",
                    "Пинг",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // TUN (весь трафик): проверка выхода в интернет через туннель.
            var r2 = await TunSmokeTest.IpifyAsync(TimeSpan.FromSeconds(4));
            sw.Stop();
            if (!r2.Success)
                throw new InvalidOperationException(r2.Error ?? "TUN test failed.");

            MessageBox.Show(this, $"TUN: OK\nВремя: {sw.ElapsedMilliseconds} мс", "Пинг", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var mode = isTun ? "TUN" : "Прокси";
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
            _stateStore.Save(_state);
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
        var running = _runner.IsRunning;
        _startBtn.Enabled = !running && _profilesCombo.SelectedItem is VlessProfile;
        _stopBtn.Enabled = running;
        _importBtn.Enabled = !running;
        _port.Enabled = !running;
        _modeCombo.Enabled = !running;
        var editTunApps = !running && string.Equals(_state.Mode, "tun_apps", StringComparison.OrdinalIgnoreCase);
        _tunAppsList.Enabled = editTunApps;
        _tunAppsAddBtn.Enabled = editTunApps;
        _tunAppsRemoveBtn.Enabled = editTunApps;

        _statusValue.Text = running ? "Запущено" : "Остановлено";
        _adminValue.Text = Elevation.IsAdministrator() ? "Администратор" : "Обычный пользователь";
        _modeValue.Text = _state.Mode.ToLowerInvariant() switch
        {
            "tun" => "TUN (весь трафик)",
            "tun_apps" => "TUN (выбранные приложения)",
            _ => "Прокси"
        };
        _profileValue.Text = _profilesCombo.SelectedItem is VlessProfile p ? p.Name : "(не выбран)";
        _portValue.Text = _state.LocalMixedPort.ToString();
    }

    private void ImportFromClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            var text = Clipboard.GetText(TextDataFormat.Text)?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            var p = VlessLinkParser.Parse(text);
            _profiles = _profileStore.Upsert(p);
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StartAsync()
    {
        if (_profilesCombo.SelectedItem is not VlessProfile p) return;

        try
        {
            UpdateButtons();

            if (string.Equals(_state.Mode, "tun_apps", StringComparison.OrdinalIgnoreCase) &&
                SingBoxConfigGenerator.NormalizeProcessPaths(_state.TunAppProcessPaths).Count == 0)
            {
                throw new InvalidOperationException(
                    "В режиме «TUN (выбранные приложения)» добавьте хотя бы один исполняемый файл (.exe).");
            }

            var isTun = AppState.IsTunMode(_state.Mode);
            if (isTun && !Elevation.IsAdministrator())
            {
                var args = $"--takeover --start --mode {_state.Mode} --profile \"{p.Id}\"";
                var ok = Elevation.RestartElevated(args);
                if (!ok)
                    throw new InvalidOperationException("TUN requires Administrator privileges (UAC was cancelled).");
                // Elevated instance will continue; this one must exit to prevent duplicate UI/tray.
                BeginInvoke(_requestExit);
                return;
            }

            await Task.Run(() =>
            {
                var cfg = SingBoxConfigGenerator.WriteConfig(_paths, p, _state);
                _runner.Start(cfg);
            });

            var isTun2 = AppState.IsTunMode(_state.Mode);
            if (!isTun2)
            {
                // Phase 2: only after proxy is reachable.
                var test = await ProxySmokeTest.HttpConnectAsync(
                    proxyHost: "127.0.0.1",
                    proxyPort: _state.LocalMixedPort,
                    targetHost: p.Host,
                    targetPort: p.Port,
                    timeout: TimeSpan.FromSeconds(3));

                if (!test.Success)
                    throw new InvalidOperationException($"Proxy smoke test failed: {test.Error}");

                var prev = _proxy.ReadCurrent();
                _proxy.Enable($"127.0.0.1:{_state.LocalMixedPort}", _state.ProxyOverride);
                _state.PreviousProxySettings = prev;
                _state.ProxyWasEnabledByUs = true;
                _stateStore.Save(_state);
            }
            else if (string.Equals(_state.Mode, "tun_apps", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(900);
                if (!_runner.IsRunning)
                    throw new InvalidOperationException("sing-box завершился при запуске TUN. Откройте вкладку «Логи» и скачайте логи для диагностики.");
                // Для split-TUN ipify из этого процесса не отражает маршрут выбранных приложений — не используем как критерий.
            }
            else
            {
                // Give TUN a moment to come up and routes/DNS to apply.
                await Task.Delay(900);
                if (!_runner.IsRunning)
                    throw new InvalidOperationException("sing-box завершился при запуске TUN. Откройте вкладку «Логи» и скачайте логи для диагностики.");

                // TUN can take a bit longer on first route/DNS application. Retry, but don't treat as fatal.
                var ok = false;
                string? lastErr = null;
                foreach (var attempt in new[] { 4, 6, 10 })
                {
                    var t = await TunSmokeTest.IpifyAsync(TimeSpan.FromSeconds(attempt));
                    ok = t.Success;
                    lastErr = t.Error;
                    if (ok) break;
                    await Task.Delay(350);
                }

                if (!ok)
                {
                    MessageBox.Show(this,
                        $"TUN started, but smoke test didn't confirm internet yet.\n\nError: {lastErr}\n\nCheck logs if browsing doesn't work.",
                        "TUN warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            _logTimer.Start();
            NotifyVpnConnectionState(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Stop();
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

    private void Stop()
    {
        try
        {
            _runner.Stop();
        }
        finally
        {
            if (_state.ProxyWasEnabledByUs)
            {
                try { _proxy.Restore(_state.PreviousProxySettings); } catch { }
                _state.ProxyWasEnabledByUs = false;
                _state.PreviousProxySettings = null;
                _stateStore.Save(_state);
            }
            UpdateButtons();
            _logTimer.Stop();
            RefreshLog();
            NotifyVpnConnectionState(false);
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
            _logBox.Text = _logStore.SnapshotText(min);
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

    private void UpdateTunAppsPanelVisibility()
    {
        _tunAppsPanel.Visible = string.Equals(_state.Mode, "tun_apps", StringComparison.OrdinalIgnoreCase);
    }

    private void SyncTunAppsListFromState()
    {
        _tunAppsList.Items.Clear();
        foreach (var s in _state.TunAppProcessPaths ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(s))
                _tunAppsList.Items.Add(s.Trim());
        }
    }

    private void PersistTunAppsFromList()
    {
        _state.TunAppProcessPaths = _tunAppsList.Items.Cast<string>().ToList();
        _stateStore.Save(_state);
    }

    private void AddTunAppExecutable()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Выберите исполняемый файл",
            Filter = "Приложения (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        var path = ofd.FileName.Trim();
        if (path.Length == 0) return;
        var dup = _tunAppsList.Items.Cast<string>().Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        if (!dup)
            _tunAppsList.Items.Add(path);
        PersistTunAppsFromList();
    }

    private void RemoveSelectedTunApp()
    {
        if (_tunAppsList.SelectedIndex < 0) return;
        _tunAppsList.Items.RemoveAt(_tunAppsList.SelectedIndex);
        PersistTunAppsFromList();
    }
}


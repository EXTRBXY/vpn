using NothingVpn.Application.Models;

namespace NothingVpn.Tray.Internal.UI;

internal sealed class ConnectionControlPanelControl : UserControl
{
    private readonly ComboBox _profiles = new();
    private readonly ComboBox _mode = new();
    private readonly NumericUpDown _port = new();
    private readonly Button _profilesButton = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();

    public event EventHandler? ProfileChanged;
    public event EventHandler? PortChanged;
    public event EventHandler? ModeChanged;
    public event EventHandler? ProfilesRequested;
    public event EventHandler? PingRequested;
    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;

    public VpnProfile? SelectedProfile => _profiles.SelectedItem as VpnProfile;
    public int Port => (int)_port.Value;
    public int ModeIndex { get => _mode.SelectedIndex; set => _mode.SelectedIndex = value; }

    public ConnectionControlPanelControl()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(UiMetrics.Space12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(Caption("Профиль"), 0, 0);
        _profiles.DropDownStyle = ComboBoxStyle.DropDownList;
        _profiles.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _profiles.MinimumSize = new Size(UiMetrics.MinInputWidth, 0);
        _profiles.SelectedIndexChanged += (_, _) => ProfileChanged?.Invoke(this, EventArgs.Empty);
        layout.Controls.Add(_profiles, 1, 0);
        _profilesButton.Text = "Профили";
        _profilesButton.AutoSize = true;
        _profilesButton.Click += (_, _) => ProfilesRequested?.Invoke(this, EventArgs.Empty);
        var ping = new Button { Text = "Пинг", AutoSize = true };
        ping.Click += (_, _) => PingRequested?.Invoke(this, EventArgs.Empty);
        layout.Controls.Add(Actions(_profilesButton, ping), 2, 0);

        layout.Controls.Add(Caption("Режим"), 0, 1);
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _mode.MinimumSize = new Size(UiMetrics.MinInputWidth, 0);
        _mode.Items.AddRange(new object[] { "Прокси", "TUN (весь трафик)", "TUN (выбранные приложения)" });
        _mode.SelectedIndexChanged += (_, _) => ModeChanged?.Invoke(this, EventArgs.Empty);
        layout.Controls.Add(_mode, 1, 1);
        _startButton.Text = "Старт";
        _startButton.AutoSize = true;
        _startButton.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        _stopButton.Text = "Стоп";
        _stopButton.AutoSize = true;
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        layout.Controls.Add(Actions(_startButton, _stopButton), 2, 1);

        layout.Controls.Add(Caption("Локальный порт"), 0, 2);
        _port.Minimum = 1;
        _port.Maximum = 65535;
        _port.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _port.ValueChanged += (_, _) => PortChanged?.Invoke(this, EventArgs.Empty);
        layout.Controls.Add(_port, 1, 2);
    }

    public void LoadProfiles(IReadOnlyList<VpnProfile> profiles, VpnProfile? selected)
    {
        _profiles.DataSource = profiles.ToList();
        _profiles.DisplayMember = nameof(VpnProfile.Name);
        if (selected is not null) _profiles.SelectedItem = selected;
    }

    public void SelectProfile(VpnProfile profile) => _profiles.SelectedItem = profile;
    public void SetPort(int port) => _port.Value = Math.Clamp(port, 1, 65535);

    public void ApplyAvailability(bool canStart, bool canStop, bool canEdit)
    {
        _startButton.Enabled = canStart;
        _stopButton.Enabled = canStop;
        _profilesButton.Enabled = canEdit;
        _profiles.Enabled = canEdit;
        _port.Enabled = canEdit;
        _mode.Enabled = canEdit;
    }

    public void DisableStartStop()
    {
        _startButton.Enabled = false;
        _stopButton.Enabled = false;
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };

    private static FlowLayoutPanel Actions(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(6, 0, 0, 0)
        };
        panel.Controls.AddRange(controls);
        return panel;
    }
}

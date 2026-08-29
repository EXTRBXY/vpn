using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Tray.Internal.Diagnostics;

namespace NothingVpn.Tray.Internal.UI;

internal sealed class LogPanelControl : UserControl
{
    private readonly InMemoryLogStore _store;
    private readonly AppLogger _logger;
    private readonly TextBox _logBox;
    private readonly ComboBox _filter;
    private readonly CheckBox _debug;
    private readonly System.Windows.Forms.Timer _timer;
    private int _lastVersion = -1;
    private int _lastMinLevel = -1;

    public event EventHandler? DebugLogsChanged;

    public bool DebugLogs
    {
        get => _debug.Checked;
        set => _debug.Checked = value;
    }

    public LogPanelControl(InMemoryLogStore store, AppLogger logger)
    {
        _store = store;
        _logger = logger;
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(UiMetrics.Space8),
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        root.Controls.Add(top, 0, 0);
        top.Controls.Add(new Label { Text = "Уровень", AutoSize = true, Margin = new Padding(6, 8, 6, 0) });

        _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _filter.Items.AddRange(new object[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR" });
        _filter.SelectedIndex = 2;
        _filter.SelectedIndexChanged += (_, _) => RefreshLog();
        top.Controls.Add(_filter);

        _debug = new CheckBox { Text = "Debug", AutoSize = true, Margin = new Padding(12, 6, 6, 0) };
        _debug.CheckedChanged += (_, _) => DebugLogsChanged?.Invoke(this, EventArgs.Empty);
        top.Controls.Add(_debug);

        var copy = new Button { Text = "Копировать", AutoSize = true, Margin = new Padding(12, 4, 6, 0) };
        copy.Click += (_, _) => CopyLogs();
        top.Controls.Add(copy);
        var download = new Button { Text = "Скачать…", AutoSize = true, Margin = new Padding(6, 4, 6, 0) };
        download.Click += (_, _) => ExportLogs();
        top.Controls.Add(download);

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9f)
        };
        root.Controls.Add(_logBox, 0, 1);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshLog();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void RefreshNow() => RefreshLog();

    private void RefreshLog()
    {
        if (!Visible) return;
        var min = SelectedMinLevel();
        if (min == _lastMinLevel && _store.TryGetVersion(out var current) && current == _lastVersion) return;
        var text = _store.SnapshotText(min, out var version);
        if (version == _lastVersion && min == _lastMinLevel) return;
        _lastVersion = version;
        _lastMinLevel = min;
        const int maxChars = 300_000;
        if (text.Length > maxChars) text = text[^maxChars..];
        var currentText = _logBox.Text;
        if (text.StartsWith(currentText, StringComparison.Ordinal)) _logBox.AppendText(text[currentText.Length..]);
        else _logBox.Text = text;
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private int SelectedMinLevel() => _filter.SelectedIndex switch
    {
        0 => 0,
        1 => 1,
        3 => 3,
        4 => 4,
        _ => 2
    };

    private void CopyLogs()
    {
        try
        {
            if (_logBox.TextLength == 0) return;
            Clipboard.SetText(_logBox.Text, TextDataFormat.Text);
            _logger.Debug("app/ui", "Логи скопированы в буфер обмена.");
        }
        catch (Exception ex)
        {
            _logger.Error("app/ui", ex, "Копирование логов завершилось ошибкой.");
            MessageBox.Show(this, ex.Message, "Копирование не удалось", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportLogs()
    {
        try
        {
            var text = _store.SnapshotAll();
            if (text.Length == 0)
            {
                MessageBox.Show(this, "Логи пустые.", "Скачать логи", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var dialog = new SaveFileDialog
            {
                Title = "Скачать логи",
                Filter = "Текст (*.txt)|*.txt|Все файлы (*.*)|*.*",
                FileName = $"nothingvpn-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dialog.FileName, text);
            _logger.Info("app/ui", $"Логи экспортированы: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            _logger.Error("app/ui", ex, "Экспорт логов завершился ошибкой.");
            MessageBox.Show(this, ex.Message, "Скачать логи не удалось", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

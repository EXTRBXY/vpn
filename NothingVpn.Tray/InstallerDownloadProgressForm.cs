using NothingVpn.Tray.Internal.Updates;

namespace NothingVpn.Tray;

internal sealed class InstallerDownloadProgressForm : Form
{
    private readonly string _downloadUrl;
    private readonly string _destPath;
    private readonly CancellationTokenSource _cts = new();
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Button _cancelButton;
    private InstallerDownloader.Result _result = new(false, "Прервано.");
    private bool _downloadFinished;

    private InstallerDownloadProgressForm(string downloadUrl, string destPath, string versionLabel)
    {
        _downloadUrl = downloadUrl;
        _destPath = destPath;

        Text = "Скачивание обновления";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(340, 0);
        MaximumSize = new Size(520, Math.Max(220, (Screen.PrimaryScreen?.WorkingArea.Height ?? 900) - 48));
        Padding = new Padding(0);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 14, 14, 10),
            Dock = DockStyle.Top
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = $"Загрузка обновления Nothing VPN {versionLabel}",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0, 0, 0, 10)
        };

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Height = 22,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 35
        };

        _statusLabel = new Label
        {
            Text = "Подготовка…",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0, 0, 0, 6)
        };

        var footer = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        _cancelButton = new Button { Text = "Отмена", AutoSize = true };
        _cancelButton.Click += (_, _) =>
        {
            _cancelButton.Enabled = false;
            _cts.Cancel();
        };
        footer.Controls.Add(_cancelButton);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(_progressBar, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        root.Controls.Add(footer, 0, 3);

        Controls.Add(root);

        FormClosing += OnFormClosing;
        Shown += OnShown;
    }

    public InstallerDownloader.Result DownloadResult => _result;

    public static InstallerDownloader.Result RunModal(IWin32Window owner, string downloadUrl, string destPath, string versionLabel)
    {
        using var form = new InstallerDownloadProgressForm(downloadUrl, destPath, versionLabel);
        form.ShowDialog(owner);
        return form.DownloadResult;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_downloadFinished)
            return;
        if (e.CloseReason == CloseReason.UserClosing)
            _cts.Cancel();
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        Shown -= OnShown;
        var progress = new Progress<InstallerDownloadProgress>(ApplyProgress);
        try
        {
            _result = await InstallerDownloader
                .DownloadAsync(_downloadUrl, _destPath, _cts.Token, progress)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _result = new InstallerDownloader.Result(false, ex.Message);
        }

        _downloadFinished = true;
        Close();
    }

    private void ApplyProgress(InstallerDownloadProgress p)
    {
        if (!IsHandleCreated || IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyProgress(p));
            return;
        }

        if (p.TotalBytes is > 0)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Maximum = 1000;
            var value = (int)Math.Min(1000, p.BytesReceived * 1000L / p.TotalBytes.Value);
            _progressBar.Value = value;
            var pct = (int)Math.Min(100, p.BytesReceived * 100L / p.TotalBytes.Value);
            _statusLabel.Text = $"{pct}% · {FormatBytes(p.BytesReceived)} из {FormatBytes(p.TotalBytes.Value)}";
        }
        else
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _statusLabel.Text = $"Загружено {FormatBytes(p.BytesReceived)}…";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} Б";
        double kb = bytes / 1024.0;
        if (kb < 1024)
            return $"{kb:0.0} КБ";
        double mb = kb / 1024.0;
        if (mb < 1024)
            return $"{mb:0.0} МБ";
        return $"{mb / 1024:0.00} ГБ";
    }
}

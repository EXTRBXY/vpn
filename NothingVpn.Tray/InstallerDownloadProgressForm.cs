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

        Text = "Загрузка обновления";
        Width = 440;
        Height = 160;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var title = new Label
        {
            Text = $"Скачивается установщик Nothing VPN {versionLabel}…",
            AutoSize = false,
            Width = 400,
            Height = 36,
            Location = new Point(16, 12),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(16, 52),
            Width = 392,
            Height = 22,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 35
        };

        _statusLabel = new Label
        {
            Text = "Подключение…",
            AutoSize = false,
            Width = 392,
            Height = 20,
            Location = new Point(16, 82),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _cancelButton = new Button
        {
            Text = "Отмена",
            Location = new Point(320, 108),
            AutoSize = true
        };
        _cancelButton.Click += (_, _) =>
        {
            _cancelButton.Enabled = false;
            _cts.Cancel();
        };

        Controls.Add(title);
        Controls.Add(_progressBar);
        Controls.Add(_statusLabel);
        Controls.Add(_cancelButton);

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
            _statusLabel.Text = $"{pct}% · {FormatBytes(p.BytesReceived)} / {FormatBytes(p.TotalBytes.Value)}";
        }
        else
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _statusLabel.Text = $"Скачано {FormatBytes(p.BytesReceived)}…";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024)
            return $"{kb:0.0} KB";
        double mb = kb / 1024.0;
        if (mb < 1024)
            return $"{mb:0.0} MB";
        return $"{mb / 1024:0.00} GB";
    }
}

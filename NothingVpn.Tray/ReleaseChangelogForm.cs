using NothingVpn.Tray.Internal.Updates;

namespace NothingVpn.Tray;

internal sealed class ReleaseChangelogForm : Form
{
    public ReleaseChangelogForm(string versionLabel, string body, bool loadFailed)
    {
        Text = loadFailed ? AppUpdateUserMessages.ChangelogTitleProblem : AppUpdateUserMessages.ChangelogTitleOk;
        Width = 560;
        Height = 480;
        MinimumSize = new Size(400, 300);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var title = new Label
        {
            Text = loadFailed
                ? AppUpdateUserMessages.ChangelogLoadFailed(versionLabel)
                : AppUpdateUserMessages.ChangelogHeading(versionLabel),
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Padding = new Padding(12, 12, 12, 8)
        };

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            Text = loadFailed
                ? ""
                : (string.IsNullOrWhiteSpace(body) ? AppUpdateUserMessages.ChangelogEmpty : body)
        };

        var ok = new Button { Text = "Закрыть", DialogResult = DialogResult.OK, AutoSize = true };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 12, 12)
        };
        footer.Controls.Add(ok);
        AcceptButton = ok;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(box, 0, 1);

        Controls.Add(root);
        Controls.Add(footer);
    }
}

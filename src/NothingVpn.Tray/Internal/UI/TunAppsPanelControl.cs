using NothingVpn.Infrastructure.TunApps;

namespace NothingVpn.Tray.Internal.UI;

internal sealed class TunAppsPanelControl : UserControl
{
    private readonly GroupBox _group;
    private readonly ListView _list;
    private readonly Button _add;
    private readonly Button _browse;
    private readonly Button _remove;
    private readonly ImageList _icons;
    private readonly TunAppIconCache _iconCache = new();

    public event EventHandler? AddRequested;
    public event EventHandler? BrowseRequested;
    public event EventHandler? RemoveRequested;

    public IEnumerable<string> Paths => _list.Items.Cast<ListViewItem>()
        .Select(item => item.Tag as string)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Cast<string>();

    public string? SelectedPath => _list.SelectedItems.Count == 1
        ? _list.SelectedItems[0].Tag as string
        : null;

    public TunAppsPanelControl()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Visible = false;

        _group = new GroupBox
        {
            Text = "TUN приложения",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, UiMetrics.Space8)
        };
        Controls.Add(_group);
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "Полные пути .exe через VPN. Дочерние процессы — отдельно.",
            AutoSize = true,
            MaximumSize = new Size(560, 0)
        }, 0, 0);
        _icons = TunAppIconCache.CreateImageList();
        _list = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            SmallImageList = _icons
        };
        _list.Columns.Add("Приложение", 160);
        _list.Columns.Add("Путь", 360);
        root.Controls.Add(_list, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        _add = new Button { Text = "Добавить", AutoSize = true };
        _browse = new Button { Text = "Указать в проводнике", AutoSize = true };
        _remove = new Button { Text = "Удалить", AutoSize = true };
        _add.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        _browse.Click += (_, _) => BrowseRequested?.Invoke(this, EventArgs.Empty);
        _remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
        buttons.Controls.AddRange([_add, _browse, _remove]);
        root.Controls.Add(buttons, 0, 2);
        _group.Controls.Add(root);
    }

    public void SetPaths(IEnumerable<string> paths)
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var path in paths)
            {
                var item = new ListViewItem(Path.GetFileNameWithoutExtension(path)) { Tag = path };
                item.SubItems.Add(path);
                item.ImageIndex = _iconCache.GetImageIndex(_icons, path);
                _list.Items.Add(item);
            }
        }
        finally { _list.EndUpdate(); }
    }

    public void SetModeVisible(bool visible) => Visible = visible;

    public void SetEditingEnabled(bool enabled)
    {
        _list.Enabled = enabled;
        _add.Enabled = enabled;
        _browse.Enabled = enabled;
        _remove.Enabled = enabled;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _icons.Dispose();
        base.Dispose(disposing);
    }
}

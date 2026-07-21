using NothingVpn.Infrastructure.TunApps;
using NothingVpn.Tray.Internal.UI;

namespace NothingVpn.Tray;

internal sealed class TunAppsPickerDialog : Form
{
    private readonly TunAppsSelectionService _selectionService;
    private readonly IReadOnlyList<string> _existingPaths;
    private readonly CancellationTokenSource _cts = new();

    private readonly TextBox _searchBox;
    private readonly TabControl _tabs;
    private readonly ListView _installedList;
    private readonly ListView _runningList;
    private readonly Button _refreshInstalledBtn;
    private readonly Button _refreshRunningBtn;
    private readonly Button _browseFileBtn;
    private readonly Button _addSelectedBtn;
    private readonly Button _cancelBtn;
    private readonly Label _statusLabel;

    private readonly ImageList _smallIcons;
    private readonly TunAppIconCache _iconCache = new();

    private IReadOnlyList<AppCandidate> _installedCandidates = Array.Empty<AppCandidate>();
    private IReadOnlyList<AppCandidate> _runningCandidates = Array.Empty<AppCandidate>();
    private List<string> _pickedFromBrowse = new();

    public IReadOnlyList<string> SelectedPaths { get; private set; } = Array.Empty<string>();

    public TunAppsPickerDialog(TunAppsSelectionService selectionService, IEnumerable<string>? existingPaths)
    {
        _selectionService = selectionService;
        _existingPaths = TunAppPathPolicy.NormalizeDistinctPaths(existingPaths);

        Text = "Добавить приложения в TUN";
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? SystemIcons.Application;
        Width = 860;
        Height = 560;
        MinimumSize = new Size(700, 450);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var header = new Label
        {
            AutoSize = true,
            Text = "Выберите приложения для режима TUN (выбранные приложения). Добавляются только валидные .exe пути."
        };
        root.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(content, 0, 1);

        var searchPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        searchPanel.Controls.Add(new Label { Text = "Поиск", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        _searchBox = new TextBox { Width = 300 };
        _searchBox.TextChanged += (_, _) => RefreshVisibleLists();
        searchPanel.Controls.Add(_searchBox);
        content.Controls.Add(searchPanel, 0, 0);

        _tabs = new TabControl { Dock = DockStyle.Fill };
        content.Controls.Add(_tabs, 0, 1);

        var installedTab = new TabPage("Установленные");
        var runningTab = new TabPage("Запущенные");
        _tabs.TabPages.Add(installedTab);
        _tabs.TabPages.Add(runningTab);

        _smallIcons = TunAppIconCache.CreateImageList();
        _installedList = BuildCandidatesListView(_smallIcons);
        _runningList = BuildCandidatesListView(_smallIcons);

        _refreshInstalledBtn = new Button { Text = "Обновить", AutoSize = true };
        _refreshRunningBtn = new Button { Text = "Обновить", AutoSize = true };
        _refreshInstalledBtn.Click += async (_, _) => await LoadInstalledAsync();
        _refreshRunningBtn.Click += async (_, _) => await LoadRunningAsync();

        installedTab.Controls.Add(BuildTabLayout(_installedList, _refreshInstalledBtn));
        runningTab.Controls.Add(BuildTabLayout(_runningList, _refreshRunningBtn));

        _statusLabel = new Label { AutoSize = true, Text = "Загрузка списков..." };
        root.Controls.Add(_statusLabel, 0, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        _cancelBtn = new Button { Text = "Отмена", AutoSize = true };
        _cancelBtn.Click += (_, _) => Close();
        _addSelectedBtn = new Button { Text = "Добавить выбранные", AutoSize = true };
        _addSelectedBtn.Click += (_, _) => AcceptSelection();
        _browseFileBtn = new Button { Text = "Указать в проводнике", AutoSize = true };
        _browseFileBtn.Click += (_, _) => BrowseExeFile();
        footer.Controls.Add(_cancelBtn);
        footer.Controls.Add(_addSelectedBtn);
        footer.Controls.Add(_browseFileBtn);
        root.Controls.Add(footer, 0, 3);

        Shown += async (_, _) =>
        {
            await LoadInstalledAsync();
            await LoadRunningAsync();
        };

        UiStyler.ApplyToForm(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cts.Cancel();
        base.Dispose(disposing);
    }

    private static Control BuildTabLayout(ListView listView, Button refreshBtn)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(listView, 0, 0);
        layout.Controls.Add(refreshBtn, 0, 1);
        return layout;
    }

    private static ListView BuildCandidatesListView(ImageList smallIcons)
    {
        var list = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            CheckBoxes = true,
            HideSelection = false,
            SmallImageList = smallIcons
        };
        list.Columns.Add("Приложение", 240);
        list.Columns.Add("Путь", 520);
        return list;
    }

    private async Task LoadInstalledAsync()
    {
        try
        {
            SetLoadingState("Загрузка установленных приложений...");
            _installedCandidates = await _selectionService.GetInstalledCandidatesAsync(_existingPaths, _cts.Token);
            RefreshVisibleLists();
            _statusLabel.Text = $"Установленные: {_installedCandidates.Count}";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _statusLabel.Text = "Не удалось загрузить список установленных приложений.";
        }
    }

    private async Task LoadRunningAsync()
    {
        try
        {
            SetLoadingState("Загрузка запущенных процессов...");
            _runningCandidates = await _selectionService.GetRunningCandidatesAsync(_existingPaths, _cts.Token);
            RefreshVisibleLists();
            _statusLabel.Text = $"Запущенные: {_runningCandidates.Count}";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _statusLabel.Text = "Не удалось загрузить список запущенных приложений.";
        }
    }

    private void SetLoadingState(string status)
    {
        _statusLabel.Text = status;
        _addSelectedBtn.Enabled = false;
    }

    private void RefreshVisibleLists()
    {
        PopulateList(_installedList, _installedCandidates, _searchBox.Text);
        PopulateList(_runningList, _runningCandidates, _searchBox.Text);
        _addSelectedBtn.Enabled = true;
    }

    private void PopulateList(ListView list, IReadOnlyList<AppCandidate> source, string? filter)
    {
        var query = (filter ?? string.Empty).Trim();
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var candidate in source)
            {
                if (query.Length != 0 &&
                    candidate.DisplayName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    candidate.ExePath.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var item = new ListViewItem(candidate.DisplayName);
                item.SubItems.Add(candidate.ExePath);
                item.Tag = candidate;
                item.ImageIndex = 0;
                _iconCache.QueueImageLoad(_smallIcons, candidate.ExePath, this, idx =>
                {
                    if (item.ListView is null || item.ListView.IsDisposed) return;
                    item.ImageIndex = idx;
                });
                list.Items.Add(item);
            }
        }
        finally
        {
            list.EndUpdate();
        }
    }

    private void BrowseExeFile()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Выберите исполняемый файл",
            Filter = "Приложения (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK)
            return;

        if (!TunAppPathPolicy.TryNormalizeExePath(ofd.FileName, out var normalized))
        {
            MessageBox.Show(this, "Выбран невалидный путь .exe.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_pickedFromBrowse.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        _pickedFromBrowse.Add(normalized);
        _statusLabel.Text = $"Добавлено вручную: {_pickedFromBrowse.Count}";
    }

    private void AcceptSelection()
    {
        var selected = new List<string>();
        selected.AddRange(_pickedFromBrowse);
        selected.AddRange(GetCheckedPaths(_installedList));
        selected.AddRange(GetCheckedPaths(_runningList));

        SelectedPaths = TunAppPathPolicy.NormalizeDistinctPaths(selected);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static IEnumerable<string> GetCheckedPaths(ListView list)
    {
        foreach (ListViewItem item in list.CheckedItems)
        {
            if (item.Tag is not AppCandidate candidate)
                continue;
            yield return candidate.ExePath;
        }
    }
}

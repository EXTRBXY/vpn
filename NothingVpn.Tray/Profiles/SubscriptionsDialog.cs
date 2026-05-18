using System.Drawing;
using System.Windows.Forms;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Tray.Internal.UI;

namespace NothingVpn.Tray;

internal sealed class SubscriptionsDialog : Form
{
    private readonly ISubscriptionService _subscriptionService;

    private readonly ListView _list;
    private readonly Button _addBtn;
    private readonly Button _editBtn;
    private readonly Button _deleteBtn;
    private readonly Button _refreshBtn;
    private readonly Button _refreshAllBtn;

    private IReadOnlyList<SubscriptionModel> _subscriptions = Array.Empty<SubscriptionModel>();

    private SubscriptionModel? Selected =>
        _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as SubscriptionModel : null;

    public SubscriptionsDialog(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;

        Text = "Подписки";
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? SystemIcons.Application;
        Width = 900;
        Height = 520;
        MinimumSize = new Size(720, 400);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Подписки 3x-ui и совместимые URL (/sub/). Синхронизируются только VLESS-узлы."
        }, 0, 0);

        _list = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
        };
        _list.Columns.Add("Название", 160);
        _list.Columns.Add("Статус", 200);
        _list.Columns.Add("Трафик", 220);
        _list.Columns.Add("Срок", 140);
        _list.Columns.Add("Интервал (ч)", 80);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => BeginEditSelected();
        root.Controls.Add(_list, 0, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
        };

        _refreshAllBtn = new Button { Text = "Обновить все", AutoSize = true };
        _refreshAllBtn.Click += async (_, _) => await RefreshAllAsync();

        _refreshBtn = new Button { Text = "Обновить", AutoSize = true, Enabled = false };
        _refreshBtn.Click += async (_, _) => await RefreshSelectedAsync();

        _editBtn = new Button { Text = "Изменить", AutoSize = true, Enabled = false };
        _editBtn.Click += (_, _) => BeginEditSelected();

        _deleteBtn = new Button { Text = "Удалить", AutoSize = true, Enabled = false };
        _deleteBtn.Click += (_, _) => DeleteSelected();

        _addBtn = new Button { Text = "Добавить", AutoSize = true };
        _addBtn.Click += (_, _) => BeginAdd();

        footer.Controls.Add(_refreshAllBtn);
        footer.Controls.Add(_refreshBtn);
        footer.Controls.Add(_editBtn);
        footer.Controls.Add(_deleteBtn);
        footer.Controls.Add(_addBtn);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);
        UiStyler.ApplyToForm(this);
        Shown += (_, _) => Reload();
    }

    private void Reload()
    {
        _subscriptions = _subscriptionService.GetSubscriptions();
        var selectedId = Selected?.Id;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var s in _subscriptions)
            {
                var item = new ListViewItem(s.Name);
                item.SubItems.Add(SubscriptionDisplayHelper.FormatLastSync(s));
                item.SubItems.Add(SubscriptionDisplayHelper.FormatTraffic(s.UserInfo));
                item.SubItems.Add(SubscriptionDisplayHelper.FormatExpire(s.UserInfo.ExpireUtc));
                item.SubItems.Add(s.UpdateIntervalHours.ToString());
                item.Tag = s;
                _list.Items.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                for (int i = 0; i < _list.Items.Count; i++)
                {
                    if (_list.Items[i].Tag is SubscriptionModel sub &&
                        string.Equals(sub.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                    {
                        _list.Items[i].Selected = true;
                        break;
                    }
                }
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var has = Selected is not null;
        _editBtn.Enabled = has;
        _deleteBtn.Enabled = has;
        _refreshBtn.Enabled = has;
    }

    private void BeginAdd()
    {
        using var dlg = new SubscriptionUpsertDialog(_subscriptionService);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        Reload();
        _ = RefreshByIdAsync(dlg.ResultSubscriptionId);
    }

    private void BeginEditSelected()
    {
        var selected = Selected;
        if (selected is null)
            return;

        using var dlg = new SubscriptionUpsertDialog(_subscriptionService, selected);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        Reload();
    }

    private void DeleteSelected()
    {
        var selected = Selected;
        if (selected is null)
            return;

        var confirm = MessageBox.Show(
            this,
            $"Удалить подписку \"{selected.Name}\" и все её профили?",
            "Удаление подписки",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
            return;

        _subscriptionService.Delete(selected.Id);
        Reload();
    }

    private async Task RefreshSelectedAsync()
    {
        var selected = Selected;
        if (selected is null)
            return;
        await RefreshByIdAsync(selected.Id);
    }

    private async Task RefreshAllAsync()
    {
        SetBusy(true);
        try
        {
            foreach (var sub in _subscriptions)
                await RefreshByIdAsync(sub.Id, reload: false);

            Reload();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshByIdAsync(string subscriptionId, bool reload = true)
    {
        SetBusy(true);
        try
        {
            var result = await _subscriptionService.RefreshAsync(subscriptionId);
            if (reload)
                Reload();

            ShowRefreshResult(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Подписка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowRefreshResult(SubscriptionRefreshResult result)
    {
        if (!result.Success)
        {
            MessageBox.Show(this, result.Error ?? "Ошибка синхронизации.", "Подписка",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var lines = new List<string>
        {
            $"Добавлено: {result.Added}",
            $"Обновлено: {result.Updated}",
            $"Удалено: {result.Removed}",
        };
        if (result.SkippedNonVless > 0)
            lines.Add($"Пропущено (не VLESS): {result.SkippedNonVless}");
        if (result.ParseErrors.Count > 0)
            lines.Add($"Ошибок разбора: {result.ParseErrors.Count}");
        if (result.ActiveProfileCleared)
            lines.Add("Активный профиль сброшен (удалён при синхронизации).");

        MessageBox.Show(this, string.Join(Environment.NewLine, lines), "Синхронизация",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SetBusy(bool busy)
    {
        _addBtn.Enabled = !busy;
        _editBtn.Enabled = !busy && Selected is not null;
        _deleteBtn.Enabled = !busy && Selected is not null;
        _refreshBtn.Enabled = !busy && Selected is not null;
        _refreshAllBtn.Enabled = !busy;
        UseWaitCursor = busy;
    }
}

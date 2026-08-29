using System.Drawing;
using System.Windows.Forms;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Presentation;
using NothingVpn.Tray.Internal.UI;

namespace NothingVpn.Tray;

internal sealed class ProfilesDialog : Form
{
    private readonly IProfileManagementController _controller;
    private readonly ISubscriptionManagementController? _subscriptionController;

    public string? ResultActiveProfileId { get; private set; }

    private readonly ListView _profilesList;
    private readonly Button _addBtn;
    private readonly Button _subscriptionsBtn;
    private readonly Button _deleteBtn;
    private readonly Button _editBtn;

    private IReadOnlyList<VpnProfile> _profiles = Array.Empty<VpnProfile>();

    private VpnProfile? SelectedProfile =>
        _profilesList.SelectedItems.Count == 1
            ? _profilesList.SelectedItems[0].Tag as VpnProfile
            : null;

    public ProfilesDialog(IProfileService profileService, string? initialActiveProfileId)
        : this(profileService, subscriptionService: null, initialActiveProfileId)
    {
    }

    public ProfilesDialog(
        IProfileService profileService,
        ISubscriptionService? subscriptionService,
        string? initialActiveProfileId)
    {
        _controller = new ProfileManagementController(profileService, initialActiveProfileId);
        _subscriptionController = subscriptionService is null ? null : new SubscriptionManagementController(subscriptionService);

        Text = "Профили";
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? SystemIcons.Application;
        Width = 720;
        Height = 520;
        MinimumSize = new Size(560, 380);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            AutoSize = true,
            Text = "Управление профилями VLESS (vless://)."
        };
        root.Controls.Add(header, 0, 0);

        _profilesList = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
        };
        _profilesList.Columns.Add("Имя", 240);
        _profilesList.Columns.Add("Хост/порт", 180);
        _profilesList.Columns.Add("Источник", 120);
        _profilesList.SelectedIndexChanged += (_, _) => UpdateButtons();
        _profilesList.DoubleClick += (_, _) => BeginEditSelected();
        root.Controls.Add(_profilesList, 0, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        _editBtn = new Button { Text = "Изменить", AutoSize = true };
        _editBtn.Click += (_, _) => BeginEditSelected();

        _deleteBtn = new Button { Text = "Удалить", AutoSize = true, Enabled = false };
        _deleteBtn.Click += (_, _) => DeleteSelected();

        _addBtn = new Button { Text = "Добавить", AutoSize = true };
        _addBtn.Click += (_, _) => BeginAdd();

        _subscriptionsBtn = new Button { Text = "Подписки…", AutoSize = true, Enabled = _subscriptionController is not null };
        _subscriptionsBtn.Click += (_, _) => OpenSubscriptions();

        footer.Controls.Add(_editBtn);
        footer.Controls.Add(_deleteBtn);
        footer.Controls.Add(_subscriptionsBtn);
        footer.Controls.Add(_addBtn);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);

        UiStyler.ApplyToForm(this);
        Shown += (_, _) => ReloadProfiles();
    }

    private void ReloadProfiles()
    {
        var snapshot = _controller.Load();
        _profiles = snapshot.Profiles;
        ResultActiveProfileId = snapshot.ChangedActiveProfileId;

        var selectedId = SelectedProfile?.Id;
        var preferredId = _profiles.Any(p =>
            string.Equals(p.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ? selectedId
            : snapshot.ActiveProfileId;
        _profilesList.BeginUpdate();
        try
        {
            _profilesList.Items.Clear();
            foreach (var p in _profiles)
            {
                var item = new ListViewItem(p.Name);
                item.SubItems.Add($"{p.Host}:{p.Port}");
                item.SubItems.Add(string.IsNullOrWhiteSpace(p.SubscriptionId) ? "Вручную" : "Подписка");
                item.Tag = p;
                _profilesList.Items.Add(item);
            }

            SelectProfileById(preferredId);
        }
        finally
        {
            _profilesList.EndUpdate();
        }

        UpdateButtons();
    }

    private void SelectProfileById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        for (int i = 0; i < _profilesList.Items.Count; i++)
        {
            if (_profilesList.Items[i].Tag is not VpnProfile p)
                continue;
            if (!string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                continue;

            _profilesList.Items[i].Selected = true;
            _profilesList.Items[i].Focused = true;
            _profilesList.EnsureVisible(i);
            return;
        }
    }

    private void UpdateButtons()
    {
        var has = SelectedProfile is not null;
        _deleteBtn.Enabled = has;
        _editBtn.Enabled = has;
    }

    private void BeginAdd()
    {
        using var dlg = new ProfileUpsertDialog(_controller);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        ReloadProfiles();
        SelectProfileById(dlg.ResultProfileId);
    }

    private void OpenSubscriptions()
    {
        if (_subscriptionController is null)
            return;

        using var dlg = new SubscriptionsDialog(_subscriptionController);
        dlg.ShowDialog(this);
        ReloadProfiles();
    }

    private void BeginEditSelected()
    {
        var selected = SelectedProfile;
        if (selected is null)
            return;

        if (!string.IsNullOrWhiteSpace(selected.SubscriptionId))
        {
            MessageBox.Show(
                this,
                "Профиль из подписки нельзя редактировать вручную. Обновите подписку или измените узел на панели.",
                "Профиль",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dlg = new ProfileUpsertDialog(_controller, selected);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var newId = dlg.ResultProfileId;

        ReloadProfiles();

        SelectProfileById(newId);
    }

    private void DeleteSelected()
    {
        var selected = SelectedProfile;
        if (selected is null)
            return;

        var deletedId = selected.Id;
        var confirm = MessageBox.Show(
            this,
            $"Удалить профиль \"{selected.Name}\"?\nЭто действие нельзя отменить.",
            "Удаление профиля",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes)
            return;

        _controller.Delete(deletedId);
        ReloadProfiles();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // When user closes via X, keep result only if it was updated via SetActive.
        base.OnFormClosing(e);
    }
}


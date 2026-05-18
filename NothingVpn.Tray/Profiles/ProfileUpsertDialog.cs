using System.Drawing;
using System.Windows.Forms;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Tray.Internal.UI;

namespace NothingVpn.Tray;

internal sealed class ProfileUpsertDialog : Form
{
    private readonly IProfileService _profileService;
    private readonly VpnProfile? _existingProfile;

    private readonly TextBox _nameBox;
    private readonly Label _nameLabel;
    private readonly TextBox _linkBox;
    private readonly Button _saveBtn;
    private readonly Button _cancelBtn;

    private readonly TableLayoutPanel _root;

    public string ResultProfileId { get; private set; } = string.Empty;

    public ProfileUpsertDialog(IProfileService profileService)
        : this(profileService, existingProfile: null, isEdit: false)
    {
    }

    public ProfileUpsertDialog(IProfileService profileService, VpnProfile existingProfile)
        : this(profileService, existingProfile, isEdit: true)
    {
    }

    private ProfileUpsertDialog(IProfileService profileService, VpnProfile? existingProfile, bool isEdit)
    {
        _profileService = profileService;
        _existingProfile = existingProfile;

        Text = isEdit ? "Изменить профиль" : "Добавить профиль";
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? SystemIcons.Application;
        Width = 620;
        Height = 260;
        MinimumSize = new Size(520, 220);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12),
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _nameLabel = new Label { Text = "Название", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) };
        _nameBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

        _linkBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

        _saveBtn = new Button { Text = "Сохранить", AutoSize = true, Anchor = AnchorStyles.Right };
        _cancelBtn = new Button { Text = "Отмена", AutoSize = true, Anchor = AnchorStyles.Left };

        _saveBtn.Click += (_, _) => OnSave();
        _cancelBtn.Click += (_, _) => Close();

        _root.Controls.Add(_nameLabel, 0, 0);
        _root.Controls.Add(_nameBox, 1, 0);

        _root.Controls.Add(new Label
        {
            Text = "VLESS ссылка",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 6)
        }, 0, 1);
        _root.Controls.Add(_linkBox, 1, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        footer.Controls.Add(_saveBtn);
        footer.Controls.Add(_cancelBtn);
        _root.Controls.Add(footer, 0, 2);
        _root.SetColumnSpan(footer, 2);

        Controls.Add(_root);
        UiStyler.ApplyToForm(this);

        if (_existingProfile is not null && !string.IsNullOrWhiteSpace(_existingProfile.SubscriptionId))
            InitSubscriptionProfileReadOnly();
        else if (_existingProfile is null)
            InitAddFromClipboard();
        else
            InitEditFromExisting();

        AcceptButton = _saveBtn;
        CancelButton = _cancelBtn;
    }

    private void InitSubscriptionProfileReadOnly()
    {
        if (_existingProfile is null)
            return;

        _nameBox.Text = _existingProfile.Name ?? "";
        _linkBox.Text = VlessLinkFormatter.Build(_existingProfile);
        _nameBox.ReadOnly = true;
        _linkBox.ReadOnly = true;
        _saveBtn.Enabled = false;
        Text = "Профиль из подписки";
    }

    private void InitEditFromExisting()
    {
        if (_existingProfile is null)
            return;

        _nameBox.Text = _existingProfile.Name ?? "";
        _linkBox.Text = VlessLinkFormatter.Build(_existingProfile);
    }

    private void InitAddFromClipboard()
    {
        _nameBox.Text = "";
        _linkBox.Text = "";

        var clipboardText = TryGetClipboardText();
        if (string.IsNullOrWhiteSpace(clipboardText))
            return;

        if (_profileService.TryParseVlessLink(clipboardText, out var parsed))
        {
            _nameBox.Text = parsed.Name ?? "";
            _linkBox.Text = clipboardText;
        }
    }

    private static string? TryGetClipboardText()
    {
        try
        {
            if (!Clipboard.ContainsText())
                return null;
            return Clipboard.GetText(TextDataFormat.Text)?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private void OnSave()
    {
        try
        {
            var link = (_linkBox.Text ?? "").Trim();
            if (link.Length == 0)
                throw new InvalidOperationException("VLESS ссылка не задана.");

            if (!_profileService.TryParseVlessLink(link, out var parsed))
                throw new InvalidOperationException("Ссылка не распознана как vless:// (или содержит неподдерживаемые параметры).");

            var rawName = (_nameBox.Text ?? "").Trim();
            var nameOverride = rawName.Length == 0 ? null : rawName;

            if (_existingProfile is null)
            {
                var saved = _profileService.UpsertFromVlessLink(link, nameOverride);
                ResultProfileId = saved.Id;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            var oldId = _existingProfile.Id;
            var updated = _profileService.UpsertFromVlessLink(link, nameOverride);
            ResultProfileId = updated.Id;

            // Replace semantics: if stable profile id changed, delete the old one.
            if (!string.Equals(ResultProfileId, oldId, StringComparison.OrdinalIgnoreCase))
                _profileService.DeleteProfile(oldId);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Профиль", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}


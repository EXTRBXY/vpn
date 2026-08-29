using System.Drawing;
using System.Windows.Forms;
using NothingVpn.Application.Models;
using NothingVpn.Presentation;
using NothingVpn.Tray.Internal.UI;

namespace NothingVpn.Tray;

internal sealed class SubscriptionUpsertDialog : Form
{
    private readonly ISubscriptionManagementController _controller;
    private readonly SubscriptionModel? _existing;

    private readonly TextBox _nameBox;
    private readonly TextBox _urlBox;
    private readonly CheckBox _enabledBox;
    private readonly Button _saveBtn;
    private readonly Button _cancelBtn;

    public string ResultSubscriptionId { get; private set; } = string.Empty;

    public SubscriptionUpsertDialog(ISubscriptionManagementController controller, SubscriptionModel? existing = null)
    {
        _controller = controller;
        _existing = existing;

        Text = existing is null ? "Добавить подписку" : "Изменить подписку";
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? SystemIcons.Application;
        Width = 640;
        Height = 280;
        MinimumSize = new Size(520, 240);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _nameBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _urlBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _enabledBox = new CheckBox { Text = "Включена", AutoSize = true, Checked = true };

        root.Controls.Add(new Label { Text = "Название", AutoSize = true, Margin = new Padding(0, 6, 10, 6) }, 0, 0);
        root.Controls.Add(_nameBox, 1, 0);
        root.Controls.Add(new Label { Text = "URL подписки", AutoSize = true, Margin = new Padding(0, 6, 10, 6) }, 0, 1);
        root.Controls.Add(_urlBox, 1, 1);
        root.Controls.Add(_enabledBox, 1, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
        };
        _saveBtn = new Button { Text = "Сохранить", AutoSize = true };
        _cancelBtn = new Button { Text = "Отмена", AutoSize = true };
        _saveBtn.Click += (_, _) => OnSave();
        _cancelBtn.Click += (_, _) => Close();
        footer.Controls.Add(_saveBtn);
        footer.Controls.Add(_cancelBtn);
        root.Controls.Add(footer, 0, 4);
        root.SetColumnSpan(footer, 2);

        Controls.Add(root);
        UiStyler.ApplyToForm(this);

        if (_existing is not null)
        {
            _nameBox.Text = _existing.Name;
            _urlBox.Text = _existing.Url;
            _enabledBox.Checked = _existing.Enabled;
        }

        AcceptButton = _saveBtn;
        CancelButton = _cancelBtn;
    }

    private void OnSave()
    {
        try
        {
            var saved = _controller.Save(
                _existing?.Id,
                _nameBox.Text ?? "",
                _urlBox.Text ?? "",
                _enabledBox.Checked);
            ResultSubscriptionId = saved.Id;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Подписка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

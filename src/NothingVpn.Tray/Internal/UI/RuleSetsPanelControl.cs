using NothingVpn.Application.Models;

namespace NothingVpn.Tray.Internal.UI;

internal sealed class RuleSetsPanelControl : UserControl
{
    public DataGridView BuiltinGrid { get; }
    public DataGridView UserGrid { get; }
    public Button FetchOrRemoveButton { get; }
    public Button CheckUpdatesButton { get; }
    public Button OtherListsButton { get; }
    public Button AddUserButton { get; }
    public Button RemoveUserButton { get; }

    public RuleSetsPanelControl()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var builtinGroup = Group("Встроенные списки", bottomMargin: true);
        var builtinLayout = CreateLayout();
        BuiltinGrid = CreateGrid(multiSelect: true);
        builtinLayout.Controls.Add(BuiltinGrid, 0, 0);
        var builtinButtons = Buttons(wrap: true);
        FetchOrRemoveButton = new Button { Text = "Скачать", AutoSize = true, Enabled = false };
        CheckUpdatesButton = new Button { Text = "Проверить обновления…", AutoSize = true };
        OtherListsButton = new Button { Text = "Другие списки", AutoSize = true };
        builtinButtons.Controls.AddRange([FetchOrRemoveButton, CheckUpdatesButton, OtherListsButton]);
        builtinLayout.Controls.Add(builtinButtons, 0, 1);
        builtinGroup.Controls.Add(builtinLayout);

        var userGroup = Group("Пользовательские списки (.srs)", bottomMargin: false);
        var userLayout = CreateLayout();
        UserGrid = CreateGrid(multiSelect: false);
        userLayout.Controls.Add(UserGrid, 0, 0);
        var userButtons = Buttons(wrap: false);
        AddUserButton = new Button { Text = "Добавить .srs…", AutoSize = true };
        RemoveUserButton = new Button { Text = "Удалить", AutoSize = true };
        userButtons.Controls.AddRange([AddUserButton, RemoveUserButton]);
        userLayout.Controls.Add(userButtons, 0, 1);
        userGroup.Controls.Add(userLayout);

        Controls.Add(userGroup);
        Controls.Add(builtinGroup);
    }

    private static GroupBox Group(string text, bool bottomMargin) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(UiMetrics.Space12),
        Margin = bottomMargin ? new Padding(0, 0, 0, UiMetrics.Space8) : Padding.Empty
    };

    private static TableLayoutPanel CreateLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return layout;
    }

    private static FlowLayoutPanel Buttons(bool wrap) => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = wrap
    };

    private static DataGridView CreateGrid(bool multiSelect)
    {
        var grid = new BufferedDataGridView
        {
            Dock = DockStyle.Fill,
            Height = 180,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = multiSelect,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            AutoGenerateColumns = false
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Вкл",
            DataPropertyName = nameof(UserRuleSetModel.Enabled),
            Width = 44
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Имя",
            DataPropertyName = nameof(UserRuleSetModel.Name),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Действие",
            DataPropertyName = nameof(UserRuleSetModel.Action),
            Width = 90,
            FlatStyle = FlatStyle.Flat,
            DataSource = new[] { "direct", "block" }
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Файл",
            DataPropertyName = nameof(UserRuleSetModel.FileName),
            Width = 200,
            ReadOnly = true
        });
        return grid;
    }
}

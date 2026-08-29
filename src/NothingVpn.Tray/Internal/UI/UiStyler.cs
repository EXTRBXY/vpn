namespace NothingVpn.Tray.Internal.UI;

internal static class UiStyler
{
    public static void ApplyToForm(Form form)
    {
        form.BackColor = UiTheme.IsHighContrast ? SystemColors.Control : UiTheme.SurfaceAlt;
        form.ForeColor = UiTheme.TextPrimary;
        form.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        ParentScrollWheelRelay.Install(form);
        form.SuspendLayout();
        try
        {
            ApplyToChildren(form.Controls);
        }
        finally
        {
            form.ResumeLayout(false);
            form.PerformLayout();
        }
    }

    private static void ApplyToChildren(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case Button button:
                    button.MinimumSize = new Size(0, UiMetrics.MinButtonHeight);
                    button.FlatStyle = FlatStyle.System;
                    break;
                case TextBox textBox:
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox comboBox:
                    comboBox.IntegralHeight = false;
                    break;
                case NumericUpDown numeric:
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case GroupBox group:
                    group.Padding = new Padding(UiMetrics.Space12);
                    break;
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
            }

            if (control.Controls.Count > 0)
                ApplyToChildren(control.Controls);
        }
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = UiTheme.IsHighContrast ? SystemColors.Window : UiTheme.Surface;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = UiTheme.IsHighContrast ? SystemColors.ControlDark : UiTheme.Border;
        grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.IsHighContrast ? SystemColors.Control : UiTheme.SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = UiTheme.IsHighContrast ? SystemColors.Highlight : Color.FromArgb(225, 238, 255);
        grid.DefaultCellStyle.SelectionForeColor = UiTheme.IsHighContrast ? SystemColors.HighlightText : UiTheme.TextPrimary;
    }
}

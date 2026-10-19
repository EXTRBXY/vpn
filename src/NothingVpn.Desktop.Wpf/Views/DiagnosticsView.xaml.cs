using System.Windows;

namespace NothingVpn.Desktop.Wpf;

public partial class DiagnosticsView : System.Windows.Controls.UserControl
{
    public DiagnosticsView() => InitializeComponent();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DiagnosticsViewModel vm || string.IsNullOrEmpty(vm.LogText)) return;
        try
        {
            System.Windows.Clipboard.SetText(vm.LogText);
        }
        catch (Exception ex)
        {
            vm.ShowError(ex.Message);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DiagnosticsViewModel vm) return;
        try
        {
            var text = vm.FullLogText();
            if (text.Length == 0)
            {
                vm.ShowError("Журнал пуст.");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Текст (*.txt)|*.txt",
                FileName = $"nothingvpn-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt"
            };
            if (dialog.ShowDialog() == true)
                vm.SaveFullLog(dialog.FileName, text);
        }
        catch (Exception ex)
        {
            vm.ShowError(ex.Message);
        }
    }
}

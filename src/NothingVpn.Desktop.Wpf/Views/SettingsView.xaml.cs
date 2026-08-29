namespace NothingVpn.Desktop.Wpf;
public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();
    private void OnAddTunApp(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Приложения (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() == true && DataContext is SettingsViewModel vm) vm.AddTunApp(dialog.FileName);
    }
    private void OnRemoveTunApp(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.RemoveSelectedTunApp();
    }
}

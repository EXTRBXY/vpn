namespace NothingVpn.Desktop.Wpf;
public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();
    private void OnSettingsMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        SettingsScroll.ScrollToVerticalOffset(SettingsScroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }
    private void OnAddTunApp(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Приложения (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() == true && DataContext is SettingsViewModel vm) vm.AddTunApp(dialog.FileName);
    }
    private void OnFindTunApp(object sender, System.Windows.RoutedEventArgs e)
    {
        if(DataContext is not SettingsViewModel vm)return;
        var dialog=new TunAppPickerWindow{Owner=System.Windows.Window.GetWindow(this),DataContext=vm};
        if(dialog.ShowDialog()==true&&dialog.SelectedPath is not null)vm.AddTunApp(dialog.SelectedPath);
    }
    private void OnRemoveTunApp(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.RemoveSelectedTunApp();
    }
    private void OnImportRuleSet(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Rule-set (*.srs)|*.srs", CheckFileExists = true };
        if (dialog.ShowDialog() == true && DataContext is SettingsViewModel vm) vm.ImportRuleSet(dialog.FileName);
    }
    private void OnRemoveRuleSet(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is SettingsViewModel vm) vm.RemoveSelectedRuleSet(); }
    private async void OnDownloadBuiltin(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is SettingsViewModel vm) await vm.DownloadSelectedBuiltinAsync(); }
    private void OnRemoveBuiltin(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is SettingsViewModel vm) vm.RemoveSelectedBuiltin(); }
    private void OnOpenRuleCatalog(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(vm.RuleSetCatalogUrl) { UseShellExecute = true });
    }
    private void OnSaveRuleSets(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is SettingsViewModel vm) vm.SaveRuleSets(); }
    private void OnDnsPreset(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is System.Windows.Controls.ComboBox { SelectedItem: System.Windows.Controls.ComboBoxItem item } && item.Tag is string preset)
            vm.ApplyDnsPreset(preset);
    }
}

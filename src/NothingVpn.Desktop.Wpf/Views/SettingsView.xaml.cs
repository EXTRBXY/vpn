namespace NothingVpn.Desktop.Wpf;
public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();
    private void RunAction(Action<SettingsViewModel> action)
    {
        if (DataContext is not SettingsViewModel vm) return;
        try { action(vm); }
        catch (Exception ex) { vm.ShowError(ex.Message); }
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
        if (dialog.ShowDialog() == true) RunAction(vm => vm.ImportRuleSet(dialog.FileName));
    }
    private void OnRemoveRuleSet(object sender, System.Windows.RoutedEventArgs e) => RunAction(vm => vm.RemoveSelectedRuleSet());
    private async void OnDownloadBuiltin(object sender, System.Windows.RoutedEventArgs e) { if (DataContext is SettingsViewModel vm) await vm.DownloadSelectedBuiltinAsync(); }
    private void OnRemoveBuiltin(object sender, System.Windows.RoutedEventArgs e) => RunAction(vm => vm.RemoveSelectedBuiltin());
    private void OnOpenRuleCatalog(object sender, System.Windows.RoutedEventArgs e)
    {
        RunAction(vm => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(vm.RuleSetCatalogUrl) { UseShellExecute = true }));
    }
}

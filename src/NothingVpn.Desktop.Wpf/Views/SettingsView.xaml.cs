namespace NothingVpn.Desktop.Wpf;
public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();
    private void OnSettingsMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        var source = e.OriginalSource as System.Windows.DependencyObject;
        while (source is not null && source != SettingsScroll)
        {
            if (source is System.Windows.Controls.ScrollViewer inner)
            {
                // Keep normal scrolling inside long lists; pass the wheel to the page at their edges.
                if (e.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight)
                    return;
                e.Handled = true;
                SettingsScroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent
                });
                return;
            }
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : System.Windows.LogicalTreeHelper.GetParent(source);
        }
    }
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
    private async void OnBuiltinEnabledClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm ||
            sender is not System.Windows.Controls.CheckBox checkbox ||
            checkbox.DataContext is not NothingVpn.Application.Models.UserRuleSetModel ruleSet) return;
        try { await vm.SetBuiltinEnabledAsync(ruleSet, checkbox.IsChecked == true); }
        catch (Exception ex) { vm.ShowError(ex.Message); }
        finally { checkbox.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.UpdateTarget(); }
    }
    private void OnRemoveBuiltin(object sender, System.Windows.RoutedEventArgs e) => RunAction(vm => vm.RemoveSelectedBuiltin());
    private void OnOpenRuleCatalog(object sender, System.Windows.RoutedEventArgs e)
    {
        RunAction(vm => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(vm.RuleSetCatalogUrl) { UseShellExecute = true }));
    }
}

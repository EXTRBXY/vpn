using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NothingVpn.Application.Models;

namespace NothingVpn.Desktop.Wpf;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void OnSettingsMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source != SettingsScroll)
        {
            if (source is ScrollViewer inner)
            {
                // Keep normal scrolling inside long lists; pass the wheel to the page at their edges.
                if (e.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight)
                    return;
                e.Handled = true;
                SettingsScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = Mouse.MouseWheelEvent
                });
                return;
            }
            source = source is Visual or Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
    }

    private void RunAction(Action<SettingsViewModel> action)
    {
        if (ViewModel is not { } vm) return;
        try { action(vm); }
        catch (Exception ex) { vm.ShowError(ex.Message); }
    }

    private void OnAddTunApp(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Приложения (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() == true && ViewModel is { } vm)
            vm.Apps.AddTunApp(dialog.FileName);
    }

    private void OnFindTunApp(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var dialog = new TunAppPickerWindow { Owner = Window.GetWindow(this), DataContext = vm.Apps };
        if (dialog.ShowDialog() == true && dialog.SelectedPath is not null)
            vm.Apps.AddTunApp(dialog.SelectedPath);
    }

    private void OnRemoveTunApp(object sender, RoutedEventArgs e) => ViewModel?.Apps.RemoveSelectedTunApp();

    private void OnImportRuleSet(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Rule-set (*.srs)|*.srs", CheckFileExists = true };
        if (dialog.ShowDialog() == true)
            RunAction(vm => vm.RuleSets.ImportRuleSet(dialog.FileName));
    }

    private void OnRemoveRuleSet(object sender, RoutedEventArgs e) => RunAction(vm => vm.RuleSets.RemoveSelectedRuleSet());

    private async void OnDownloadBuiltin(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await vm.RuleSets.DownloadSelectedBuiltinAsync();
    }

    private async void OnBuiltinEnabledClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm ||
            sender is not System.Windows.Controls.CheckBox checkbox ||
            checkbox.DataContext is not UserRuleSetModel ruleSet) return;
        try { await vm.RuleSets.SetBuiltinEnabledAsync(ruleSet, checkbox.IsChecked == true); }
        catch (Exception ex) { vm.ShowError(ex.Message); }
        finally { checkbox.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget(); }
    }

    private void OnRemoveBuiltin(object sender, RoutedEventArgs e) => RunAction(vm => vm.RuleSets.RemoveSelectedBuiltin());

    private void OnOpenRuleCatalog(object sender, RoutedEventArgs e) =>
        RunAction(vm => Process.Start(new ProcessStartInfo(vm.RuleSets.RuleSetCatalogUrl) { UseShellExecute = true }));
}

using NothingVpn.Infrastructure.TunApps;
namespace NothingVpn.Desktop.Wpf;
public partial class TunAppPickerWindow:System.Windows.Window
{
    private IReadOnlyList<AppCandidate> _all=[]; public string? SelectedPath{get;private set;}
    public TunAppPickerWindow(){InitializeComponent();Loaded+=async(_,_)=>{_all=await ((SettingsViewModel)DataContext).FindTunAppsAsync();Refresh();};}
    private void OnSearch(object s,System.Windows.Controls.TextChangedEventArgs e)=>Refresh();
    private void Refresh(){var q=SearchBox.Text.Trim();AppsList.ItemsSource=_all.Where(x=>q.Length==0||x.DisplayName.Contains(q,StringComparison.CurrentCultureIgnoreCase)||x.ExePath.Contains(q,StringComparison.OrdinalIgnoreCase));}
    private void OnAccept(object s,System.Windows.RoutedEventArgs e){if(AppsList.SelectedItem is not AppCandidate c)return;SelectedPath=c.ExePath;DialogResult=true;}
    private void OnCancel(object s,System.Windows.RoutedEventArgs e)=>DialogResult=false;
}

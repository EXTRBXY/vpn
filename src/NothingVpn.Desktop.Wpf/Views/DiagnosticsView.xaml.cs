namespace NothingVpn.Desktop.Wpf; public partial class DiagnosticsView:System.Windows.Controls.UserControl
{
 public DiagnosticsView()=>InitializeComponent();
 private void OnCopy(object s,System.Windows.RoutedEventArgs e){if(DataContext is DiagnosticsViewModel vm&&!string.IsNullOrEmpty(vm.LogText))System.Windows.Clipboard.SetText(vm.LogText);}
 private void OnExport(object s,System.Windows.RoutedEventArgs e){if(DataContext is not DiagnosticsViewModel vm||string.IsNullOrEmpty(vm.AllLogs))return;var d=new Microsoft.Win32.SaveFileDialog{Filter="Текст (*.txt)|*.txt",FileName=$"nothingvpn-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt"};if(d.ShowDialog()==true)System.IO.File.WriteAllText(d.FileName,vm.AllLogs);}
}

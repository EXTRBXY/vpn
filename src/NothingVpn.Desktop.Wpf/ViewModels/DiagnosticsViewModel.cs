using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;
public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    private readonly IConnectionDiagnosticController _controller; private readonly InMemoryLogStore _logs;
    private readonly Func<string> _mode; private readonly Func<bool> _running; private string _result=""; private string _logText="";
    public DiagnosticsViewModel(IConnectionDiagnosticController controller, InMemoryLogStore logs, Func<string> mode, Func<bool> running)
    {
        _controller=controller; _logs=logs; _mode=mode; _running=running; RunCommand=new AsyncRelayCommand(RunAsync); ClearCommand=new RelayCommand(()=>{_logs.Clear(); Refresh();});
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)}; timer.Tick+=(_,_)=>Refresh(); timer.Start(); Refresh();
    }
    public event PropertyChangedEventHandler? PropertyChanged; public ICommand RunCommand{get;} public ICommand ClearCommand{get;}
    public string Result { get=>_result; private set{_result=value;OnChanged();} } public string LogText { get=>_logText;private set{_logText=value;OnChanged();} }
    private async Task RunAsync(){var r=await _controller.RunAsync(_mode(),_running());Result=r.Message;}
    private void Refresh()=>LogText=_logs.SnapshotAll(); private void OnChanged([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
}

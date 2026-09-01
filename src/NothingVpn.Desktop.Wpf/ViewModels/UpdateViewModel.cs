using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;
public sealed class UpdateViewModel:INotifyPropertyChanged
{
    private readonly IAppUpdateController _controller; private readonly IInstallerUpdateService _installer; private readonly IInstallerLaunchService _launcher; private AppStateModel _state; private readonly ISettingsService _settings; private readonly Action _exit;
    private AppReleaseModel? _release; private string _status=""; private int _progress;
    private int _checkRunning;
    public UpdateViewModel(IAppUpdateController controller,IInstallerUpdateService installer,IInstallerLaunchService launcher,ISettingsService settings,AppStateModel state,Action exit)
    { _controller=controller;_installer=installer;_launcher=launcher;_settings=settings;_state=state;_exit=exit;CheckCommand=new AsyncRelayCommand(CheckAsync);InstallCommand=new AsyncRelayCommand(InstallAsync,()=>_release is not null); }
    public event PropertyChangedEventHandler? PropertyChanged; public ICommand CheckCommand{get;} public ICommand InstallCommand{get;}
    public string CurrentVersion=>GetVersion(); public string Status{get=>_status;private set{_status=value;Changed();}} public int Progress{get=>_progress;private set{_progress=value;Changed();}}
    public async Task CheckAsync()
    {
        if (Interlocked.Exchange(ref _checkRunning, 1) == 1) return;
        try
        {
            Status="Проверка…";_state=_settings.GetState();var result=await _controller.CheckAsync(_state,GetVersion());_release=result.AvailableRelease;Status=!result.Succeeded?"Не удалось проверить обновления.":_release is null?"Установлена актуальная версия.":$"Доступна версия {_release.Semver}.";(InstallCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
        finally { Interlocked.Exchange(ref _checkRunning, 0); }
    }
    public async Task CheckIfDueAsync(){_state=_settings.GetState();if(_controller.IsPeriodicCheckDue(_state,DateTimeOffset.UtcNow))await CheckAsync();}
    public async Task CheckAtStartupAsync(){_installer.CleanupOldInstallers();_state=_settings.GetState();_controller.RecordInstalledVersion(_state,GetVersion());await CheckIfDueAsync();}
    private async Task InstallAsync()
    {
        if (_release is null) return;
        Status = "Загрузка обновления…";
        try
        {
            _launcher.EnsureLaunchAllowed();
            var p = new Progress<InstallerDownloadProgressModel>(x => Progress = x.TotalBytes is > 0 ? (int)(x.BytesReceived * 100 / x.TotalBytes.Value) : 0);
            var r = await _installer.DownloadAsync(_release, p);
            if (!r.Success || string.IsNullOrWhiteSpace(r.InstallerPath))
            {
                Status = r.Error ?? "Не удалось загрузить обновление.";
                return;
            }
            Status = "Подготовка установки…";
            await Task.Run(() => _launcher.ScheduleAfterApplicationExits(r.InstallerPath));
            _exit();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }
    private static string GetVersion(){var v=Assembly.GetEntryAssembly()?.GetName().Version;return v is null?"0.0.0":$"{v.Major}.{v.Minor}.{Math.Max(0,v.Build)}";}
    private void Changed([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
}

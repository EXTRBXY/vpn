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
    private readonly IAppUpdateController _controller; private readonly IInstallerUpdateService _installer; private readonly IInstallerLaunchService _launcher; private AppStateModel _state; private readonly ISettingsService _settings; private readonly Action _exit; private readonly Action<AppReleaseModel>? _notifyAvailable;
    private AppReleaseModel? _release; private string _status=""; private int _progress;
    private int _operationRunning;
    public UpdateViewModel(IAppUpdateController controller,IInstallerUpdateService installer,IInstallerLaunchService launcher,ISettingsService settings,AppStateModel state,Action exit,Action<AppReleaseModel>? notifyAvailable=null)
    { _controller=controller;_installer=installer;_launcher=launcher;_settings=settings;_state=state;_exit=exit;_notifyAvailable=notifyAvailable;CheckCommand=new AsyncRelayCommand(CheckAsync,()=>_operationRunning == 0);InstallCommand=new AsyncRelayCommand(InstallAsync,()=>_release is not null && _operationRunning == 0); }
    public event PropertyChangedEventHandler? PropertyChanged; public ICommand CheckCommand{get;} public ICommand InstallCommand{get;}
    public string CurrentVersion=>GetVersion(); public string Status{get=>_status;private set{_status=value;Changed();}} public int Progress{get=>_progress;private set{_progress=value;Changed();}}
    public async Task CheckAsync()
    {
        if (Interlocked.Exchange(ref _operationRunning, 1) == 1) return;
        RaiseCommands();
        try
        {
            Status="Проверка…";_state=_settings.GetState();var result=await _controller.CheckAsync(_state,GetVersion());_release=result.AvailableRelease;Status=!result.Succeeded?"Не удалось проверить обновления.":_release is null?"Установлена актуальная версия.":$"Доступна версия {_release.Semver}.";(InstallCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            if (_release is not null && _notifyAvailable is not null && _controller.ShouldOffer(_state, _release))
            {
                _notifyAvailable(_release);
                _controller.DismissOffer(_state, _release);
            }
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { Interlocked.Exchange(ref _operationRunning, 0); RaiseCommands(); }
    }
    public async Task CheckIfDueAsync(){try{_state=_settings.GetState();if(_controller.IsPeriodicCheckDue(_state,DateTimeOffset.UtcNow))await CheckAsync();}catch(Exception ex){Status=ex.Message;}}
    public async Task CheckAtStartupAsync(){try{_installer.CleanupOldInstallers();_state=_settings.GetState();_controller.RecordInstalledVersion(_state,GetVersion());await CheckIfDueAsync();}catch(Exception ex){Status=ex.Message;}}
    private async Task InstallAsync()
    {
        if (_release is null || Interlocked.Exchange(ref _operationRunning, 1) == 1) return;
        var release = _release;
        RaiseCommands();
        Progress = 0;
        Status = "Загрузка обновления…";
        try
        {
            _launcher.EnsureLaunchAllowed();
            var p = new Progress<InstallerDownloadProgressModel>(x => Progress = x.TotalBytes is > 0 ? (int)(x.BytesReceived * 100 / x.TotalBytes.Value) : 0);
            var r = await _installer.DownloadAsync(release, p);
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
        finally { Interlocked.Exchange(ref _operationRunning, 0); RaiseCommands(); }
    }
    private void RaiseCommands()
    {
        ((AsyncRelayCommand)CheckCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)InstallCommand).RaiseCanExecuteChanged();
    }
    private static string GetVersion(){var v=Assembly.GetEntryAssembly()?.GetName().Version;return v is null?"0.0.0":$"{v.Major}.{v.Minor}.{Math.Max(0,v.Build)}";}
    private void Changed([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
}

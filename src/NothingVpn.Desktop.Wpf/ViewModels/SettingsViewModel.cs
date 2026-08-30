using System.ComponentModel;
using System.Runtime.CompilerServices;
using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Presentation;
using System.Windows.Input;
using System.Collections.ObjectModel;

namespace NothingVpn.Desktop.Wpf;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public event EventHandler? SettingsChanged;
    private readonly IConnectionSettingsController _controller;
    private AppStateModel _state;
    private readonly NothingVpn.Application.Services.ISettingsService _settingsService;
    private readonly ITunAppsController _tunAppsController;
    private readonly IRuleSetManagementController _ruleSetController;
    private readonly NothingVpn.Application.Services.IRuleSetFileService _ruleSetFiles;
    private readonly NothingVpn.Infrastructure.TunApps.TunAppsSelectionService _tunAppSelection;
    private string? _message;
    public SettingsViewModel(IConnectionSettingsController controller, ITunAppsController tunAppsController,
        IRuleSetManagementController ruleSetController, NothingVpn.Application.Services.IRuleSetFileService ruleSetFiles,
        NothingVpn.Infrastructure.TunApps.TunAppsSelectionService tunAppSelection,
        NothingVpn.Application.Services.ISettingsService settingsService, AppStateModel state, UpdateViewModel updates)
    {
        _controller = controller; _tunAppsController = tunAppsController; _ruleSetController = ruleSetController; _ruleSetFiles = ruleSetFiles; _tunAppSelection=tunAppSelection; _settingsService=settingsService; _state = state;
        ProxyOverride = state.ProxyOverride; InterfaceName = state.TunInterfaceName;
        AddressCidr = state.TunAddressCidr; Mtu = TunSettingsPolicy.NormalizeMtu(state.TunMtu);
        Stack = TunSettingsPolicy.NormalizeStack(state.TunStack); AutoRoute = state.TunAutoRoute; StrictRoute = state.TunStrictRoute;
        DnsMode = state.DnsMode; DohServer = state.DohServer; DohPath = state.DohPath; DohSni = state.DohSni; DnsDetour = state.DnsDetour;
        DebugLogs = state.DebugLogs; LogLevel = state.SingBoxLogLevel;
        SaveCommand = new RelayCommand(Save);
        ReplaceTunApps(_tunAppsController.Normalize(state.TunAppProcessPaths));
        var rules = _ruleSetController.Load(state);
        foreach (var item in rules.Builtin) BuiltinRuleSets.Add(item);
        foreach (var item in rules.User) UserRuleSets.Add(item);
        Updates = updates;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SaveCommand { get; }
    public UpdateViewModel Updates { get; }
    public ObservableCollection<TunAppListItem> TunApps { get; } = [];
    public ObservableCollection<UserRuleSetModel> BuiltinRuleSets { get; } = [];
    public ObservableCollection<UserRuleSetModel> UserRuleSets { get; } = [];
    public UserRuleSetModel? SelectedBuiltinRuleSet { get; set; }
    public UserRuleSetModel? SelectedUserRuleSet { get; set; }
    public TunAppListItem? SelectedTunApp { get; set; }
    public void AddTunApp(string path)
    {
        _state=_settingsService.GetState();
        var saved = _tunAppsController.AddAndSave(_state, TunApps.Select(x => x.Path), [path]);
        ReplaceTunApps(saved);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task<IReadOnlyList<NothingVpn.Infrastructure.TunApps.AppCandidate>> FindTunAppsAsync()
    {
        var selectedPaths = TunApps.Select(x => x.Path).ToArray();
        var installed=_tunAppSelection.GetInstalledCandidatesAsync(selectedPaths,CancellationToken.None);
        var running=_tunAppSelection.GetRunningCandidatesAsync(selectedPaths,CancellationToken.None);
        await Task.WhenAll(installed,running);
        return installed.Result.Concat(running.Result).GroupBy(x=>x.ExePath,StringComparer.OrdinalIgnoreCase).Select(x=>x.First()).OrderBy(x=>x.DisplayName).ToList();
    }
    public void RemoveSelectedTunApp()
    {
        if (SelectedTunApp is null) return;
        _state=_settingsService.GetState();
        var saved = _tunAppsController.RemoveAndSave(_state, TunApps.Select(x => x.Path), SelectedTunApp.Path);
        ReplaceTunApps(saved);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ImportRuleSet(string sourcePath)
    {
        var imported = _ruleSetFiles.Import(sourcePath);
        UserRuleSets.Add(_ruleSetController.CreateUserRuleSet(imported.Name, imported.FileName));
        SaveRuleSets();
    }
    public void RemoveSelectedRuleSet()
    {
        if (SelectedUserRuleSet is null) return;
        _ruleSetFiles.Delete(SelectedUserRuleSet); UserRuleSets.Remove(SelectedUserRuleSet); SaveRuleSets();
    }
    public async Task DownloadSelectedBuiltinAsync()
    {
        if (SelectedBuiltinRuleSet is null) return;
        _state=_settingsService.GetState();
        var result = await _ruleSetFiles.DownloadBuiltinAsync(SelectedBuiltinRuleSet, true);
        if (!result.Success) { Message = result.Error ?? "Не удалось скачать список."; return; }
        SelectedBuiltinRuleSet.Enabled = true;
        _ruleSetController.MarkDownloaded(_state, SelectedBuiltinRuleSet, result.NewEtag);
        SaveRuleSets(); Message = result.NotModified ? "Список уже актуален." : "Список обновлён.";
    }
    public void RemoveSelectedBuiltin()
    {
        if (SelectedBuiltinRuleSet is null) return;
        _ruleSetFiles.Delete(SelectedBuiltinRuleSet);
        _state=_settingsService.GetState();
        _ruleSetController.MarkBuiltinFilesRemoved(_state, BuiltinRuleSets, UserRuleSets, [SelectedBuiltinRuleSet]);
        Message = "Файл списка удалён.";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public string RuleSetCatalogUrl => _ruleSetFiles.CatalogUrl;
    public void SaveRuleSets()
    {
        _state=_settingsService.GetState();
        _ruleSetController.Save(_state, BuiltinRuleSets, UserRuleSets); Message = "Правила сохранены.";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public string ProxyOverride { get; set; } = "";
    public string InterfaceName { get; set; } = "";
    public string AddressCidr { get; set; } = "auto";
    public int Mtu { get; set; }
    public string Stack { get; set; } = "";
    public bool AutoRoute { get; set; }
    public bool StrictRoute { get; set; }
    public string DnsMode { get; set; } = "doh";
    public string DohServer { get; set; } = "";
    public string DohPath { get; set; } = "/dns-query";
    public string DohSni { get; set; } = "";
    public string DnsDetour { get; set; } = "direct";
    public bool DebugLogs { get; set; }
    public string LogLevel { get; set; } = "warn";
    public void ApplyDnsPreset(string preset)
    {
        (DohServer, DohSni) = preset switch
        {
            "cloudflare" => ("1.1.1.1", "cloudflare-dns.com"),
            "google" => ("8.8.8.8", "dns.google"),
            "quad9" => ("9.9.9.9", "dns.quad9.net"),
            "adguard" => ("94.140.14.14", "dns.adguard.com"),
            _ => (DohServer, DohSni)
        };
        DohPath = "/dns-query";
        OnPropertyChanged(nameof(DohServer)); OnPropertyChanged(nameof(DohSni)); OnPropertyChanged(nameof(DohPath));
    }
    public string? Message { get => _message; private set { _message = value; OnPropertyChanged(); } }
    private void Save()
    {
        try
        {
            _state=_settingsService.GetState();
            _state.DebugLogs = DebugLogs;
            _state.SingBoxLogLevel = LogLevel;
            _controller.Save(_state, new ConnectionSettingsDraft(
                new ProxyConnectionSettings { ProxyOverride = ProxyOverride },
                new TunSettings { InterfaceName = InterfaceName, AddressCidr = AddressCidr, Mtu = Mtu, Stack = Stack, AutoRoute = AutoRoute, StrictRoute = StrictRoute },
                new DnsSettings { Mode = DnsMode, DohServer = DohServer, DohPath = DohPath, DohSni = DohSni, Detour = DnsDetour }));
            Message = "Настройки сохранены.";
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Message = ex.Message; }
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void ReplaceTunApps(IEnumerable<string> paths)
    {
        TunApps.Clear();
        foreach (var path in paths) TunApps.Add(TunAppListItem.FromPath(path));
    }
}

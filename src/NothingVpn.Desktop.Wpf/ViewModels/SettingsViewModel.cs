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
    private string? _dnsPreset;
    private string _dnsMode = "doh";
    private bool _dnsDetourEditable = true;
    private CancellationTokenSource? _messageCancellation;
    public SettingsViewModel(IConnectionSettingsController controller, ITunAppsController tunAppsController,
        IRuleSetManagementController ruleSetController, NothingVpn.Application.Services.IRuleSetFileService ruleSetFiles,
        NothingVpn.Infrastructure.TunApps.TunAppsSelectionService tunAppSelection,
        NothingVpn.Application.Services.ISettingsService settingsService, AppStateModel state, UpdateViewModel updates)
    {
        _controller = controller; _tunAppsController = tunAppsController; _ruleSetController = ruleSetController; _ruleSetFiles = ruleSetFiles; _tunAppSelection=tunAppSelection; _settingsService=settingsService; _state = state;
        SaveCommand = new RelayCommand(Save);
        Updates = updates;
        ApplyState(state);
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
        if (!result.Success) { ShowMessage(result.Error ?? "Не удалось скачать список.", TimeSpan.FromSeconds(6)); return; }
        SelectedBuiltinRuleSet.Enabled = true;
        _ruleSetController.MarkDownloaded(_state, SelectedBuiltinRuleSet, result.NewEtag);
        SaveRuleSets(); ShowMessage(result.NotModified ? "Список уже актуален." : "Список обновлён.");
    }
    public void RemoveSelectedBuiltin()
    {
        if (SelectedBuiltinRuleSet is null) return;
        _ruleSetFiles.Delete(SelectedBuiltinRuleSet);
        _state=_settingsService.GetState();
        _ruleSetController.MarkBuiltinFilesRemoved(_state, BuiltinRuleSets, UserRuleSets, [SelectedBuiltinRuleSet]);
        ShowMessage("Файл списка удалён.");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public string RuleSetCatalogUrl => _ruleSetFiles.CatalogUrl;
    public void SaveRuleSets()
    {
        _state=_settingsService.GetState();
        _ruleSetController.Save(_state, BuiltinRuleSets, UserRuleSets); ShowMessage("Правила сохранены.");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public string ProxyOverride { get; set; } = "";
    public string InterfaceName { get; set; } = "";
    public string AddressCidr { get; set; } = "auto";
    public int Mtu { get; set; }
    public string Stack { get; set; } = "";
    public bool AutoRoute { get; set; }
    public bool StrictRoute { get; set; }
    public string DnsMode
    {
        get => _dnsMode;
        set
        {
            if (string.Equals(_dnsMode, value, StringComparison.Ordinal)) return;
            _dnsMode = value;
            OnPropertyChanged();
            UpdateDnsDetourAvailability();
        }
    }
    public string DohServer { get; set; } = "";
    public string DohPath { get; set; } = "/dns-query";
    public string DohSni { get; set; } = "";
    public string DnsDetour { get; set; } = "direct";
    public string LogLevel { get; set; } = "warn";
    public string CloseBehavior { get; set; } = AppCloseBehavior.HideToTray;
    public bool IsDnsDetourEditable { get => _dnsDetourEditable; private set { if (_dnsDetourEditable == value) return; _dnsDetourEditable = value; OnPropertyChanged(); OnPropertyChanged(nameof(DnsDetourHint)); } }
    public string DnsDetourHint => !string.Equals(DnsMode, "doh", StringComparison.OrdinalIgnoreCase)
        ? "Маршрут используется только для защищённого DNS."
        : !DnsDetourPolicy.AllowsProxyDetour(_state.Mode)
            ? "В режиме TUN для выбранных приложений DNS всегда идёт напрямую."
            : string.Empty;
    public string? DnsPreset
    {
        get => _dnsPreset;
        set
        {
            if (string.Equals(_dnsPreset, value, StringComparison.Ordinal)) return;
            _dnsPreset = value;
            OnPropertyChanged();
            if (value is not null) ApplyDnsPreset(value);
        }
    }
    public void Reload() => ApplyState(_settingsService.GetState());
    public void ApplyDnsPreset(string preset)
    {
        var index = preset switch { "cloudflare" => 0, "google" => 1, "quad9" => 2, "adguard" => 3, _ => -1 };
        if (index < 0) return;
        var dns = DnsPolicy.ApplyPreset(index, new DnsSettings { Mode = DnsMode, Detour = DnsDetour, DohServer = DohServer, DohSni = DohSni, DohPath = DohPath });
        DohServer = dns.DohServer; DohSni = dns.DohSni; DohPath = dns.DohPath;
        OnPropertyChanged(nameof(DohServer)); OnPropertyChanged(nameof(DohSni)); OnPropertyChanged(nameof(DohPath));
    }
    public string? Message { get => _message; private set { _message = value; OnPropertyChanged(); } }
    private void Save()
    {
        try
        {
            _state=_settingsService.GetState();
            _state.SingBoxLogLevel = LogLevel;
            _state.CloseBehavior = AppCloseBehavior.Normalize(CloseBehavior);
            _controller.Save(_state, new ConnectionSettingsDraft(
                new ProxyConnectionSettings { ProxyOverride = ProxyOverride },
                new TunSettings { InterfaceName = InterfaceName, AddressCidr = AddressCidr, Mtu = Mtu, Stack = Stack, AutoRoute = AutoRoute, StrictRoute = StrictRoute },
                new DnsSettings { Mode = DnsMode, DohServer = DohServer, DohPath = DohPath, DohSni = DohSni, Detour = DnsDetour }));
            ApplyState(_settingsService.GetState());
            ShowMessage("Настройки сохранены.");
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { ShowMessage(ex.Message, TimeSpan.FromSeconds(6)); }
    }
    private async void ShowMessage(string message, TimeSpan? duration = null)
    {
        _messageCancellation?.Cancel();
        _messageCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _messageCancellation = cancellation;
        Message = message;
        try
        {
            await Task.Delay(duration ?? TimeSpan.FromSeconds(3), cancellation.Token);
            if (ReferenceEquals(_messageCancellation, cancellation))
                Message = null;
        }
        catch (OperationCanceledException) { }
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void ReplaceTunApps(IEnumerable<string> paths)
    {
        TunApps.Clear();
        foreach (var path in paths) TunApps.Add(TunAppListItem.FromPath(path));
    }
    private void ApplyState(AppStateModel state)
    {
        _state = state;
        ProxyOverride = state.ProxyOverride; InterfaceName = state.TunInterfaceName;
        AddressCidr = state.TunAddressCidr; Mtu = TunSettingsPolicy.NormalizeMtu(state.TunMtu);
        Stack = TunSettingsPolicy.NormalizeStack(state.TunStack); AutoRoute = state.TunAutoRoute; StrictRoute = state.TunStrictRoute;
        DnsMode = state.DnsMode; DohServer = state.DohServer; DohPath = state.DohPath; DohSni = state.DohSni; DnsDetour = state.DnsDetour;
        LogLevel = state.SingBoxLogLevel; CloseBehavior = AppCloseBehavior.Normalize(state.CloseBehavior);
        _dnsPreset = DnsPolicy.StateToPresetIndex(new DnsSettings { Mode = DnsMode, Detour = DnsDetour, DohServer = DohServer, DohSni = DohSni, DohPath = DohPath }) switch
        {
            0 => "cloudflare", 1 => "google", 2 => "quad9", 3 => "adguard", _ => null
        };
        ReplaceTunApps(_tunAppsController.Normalize(state.TunAppProcessPaths));
        BuiltinRuleSets.Clear(); UserRuleSets.Clear();
        var rules = _ruleSetController.Load(state);
        foreach (var item in rules.Builtin) BuiltinRuleSets.Add(item);
        foreach (var item in rules.User) UserRuleSets.Add(item);
        foreach (var property in new[] { nameof(ProxyOverride), nameof(InterfaceName), nameof(AddressCidr), nameof(Mtu), nameof(Stack), nameof(AutoRoute), nameof(StrictRoute), nameof(DnsMode), nameof(DohServer), nameof(DohPath), nameof(DohSni), nameof(DnsDetour), nameof(LogLevel), nameof(CloseBehavior), nameof(DnsPreset) })
            OnPropertyChanged(property);
        UpdateDnsDetourAvailability();
    }
    private void UpdateDnsDetourAvailability() => IsDnsDetourEditable =
        string.Equals(DnsMode, "doh", StringComparison.OrdinalIgnoreCase) && DnsDetourPolicy.AllowsProxyDetour(_state.Mode);
}

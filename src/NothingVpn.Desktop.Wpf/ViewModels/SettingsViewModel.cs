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
    private string _dohServer = "";
    private string _dohSni = "";
    private string _dohPath = "/dns-query";
    private bool _dnsDetourEditable = true;
    private bool _isConnectionRunning;
    private bool _isRuleSetBusy;
    private readonly List<UserRuleSetModel> _pendingFileDeletes = [];
    private TunAppListItem? _selectedTunApp;
    private UserRuleSetModel? _selectedBuiltinRuleSet;
    private UserRuleSetModel? _selectedUserRuleSet;
    private CancellationTokenSource? _messageCancellation;
    public SettingsViewModel(IConnectionSettingsController controller, ITunAppsController tunAppsController,
        IRuleSetManagementController ruleSetController, NothingVpn.Application.Services.IRuleSetFileService ruleSetFiles,
        NothingVpn.Infrastructure.TunApps.TunAppsSelectionService tunAppSelection,
        NothingVpn.Application.Services.ISettingsService settingsService, AppStateModel state, UpdateViewModel updates)
    {
        _controller = controller; _tunAppsController = tunAppsController; _ruleSetController = ruleSetController; _ruleSetFiles = ruleSetFiles; _tunAppSelection=tunAppSelection; _settingsService=settingsService; _state = state;
        SaveCommand = new RelayCommand(Save, () => CanEditRuleSets);
        Updates = updates;
        ApplyState(state);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SaveCommand { get; }
    public UpdateViewModel Updates { get; }
    public ObservableCollection<TunAppListItem> TunApps { get; } = [];
    public ObservableCollection<UserRuleSetModel> BuiltinRuleSets { get; } = [];
    public ObservableCollection<UserRuleSetModel> UserRuleSets { get; } = [];
    public UserRuleSetModel? SelectedBuiltinRuleSet { get => _selectedBuiltinRuleSet; set { _selectedBuiltinRuleSet = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanUseBuiltinRuleSet)); } }
    public UserRuleSetModel? SelectedUserRuleSet { get => _selectedUserRuleSet; set { _selectedUserRuleSet = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRemoveUserRuleSet)); } }
    public TunAppListItem? SelectedTunApp { get => _selectedTunApp; set { _selectedTunApp = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRemoveTunApp)); } }
    public bool CanRemoveTunApp => SelectedTunApp is not null;
    public bool CanEditRuleSets => !_isRuleSetBusy;
    public bool CanUseBuiltinRuleSet => CanEditRuleSets && SelectedBuiltinRuleSet is not null;
    public bool CanRemoveUserRuleSet => CanEditRuleSets && SelectedUserRuleSet is not null;
    public void AddTunApp(string path)
    {
        if (!_tunAppsController.TryNormalize(path, out var normalized))
        {
            ShowError("Укажите полный путь к приложению .exe.");
            return;
        }
        if (TunApps.Any(x => string.Equals(x.Path, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            ShowMessage("Это приложение уже есть в списке.");
            return;
        }
        var item = TunAppListItem.FromPath(normalized);
        TunApps.Add(item);
        SelectedTunApp = item;
        ShowMessage("Приложение добавлено. Сохраните настройки.");
    }
    public async Task<IReadOnlyList<NothingVpn.Infrastructure.TunApps.AppCandidate>> FindTunAppsAsync(CancellationToken cancellationToken = default)
    {
        var selectedPaths = TunApps.Select(x => x.Path).ToArray();
        var installed=_tunAppSelection.GetInstalledCandidatesAsync(selectedPaths,cancellationToken);
        var running=_tunAppSelection.GetRunningCandidatesAsync(selectedPaths,cancellationToken);
        await Task.WhenAll(installed,running);
        return installed.Result.Concat(running.Result).GroupBy(x=>x.ExePath,StringComparer.OrdinalIgnoreCase).Select(x=>x.First()).OrderBy(x=>x.DisplayName).ToList();
    }
    public void RemoveSelectedTunApp()
    {
        if (SelectedTunApp is null) return;
        var index = TunApps.IndexOf(SelectedTunApp);
        if (index < 0) return;
        TunApps.RemoveAt(index);
        SelectedTunApp = TunApps.Count == 0 ? null : TunApps[Math.Min(index, TunApps.Count - 1)];
        ShowMessage("Приложение удалено из списка. Сохраните настройки.");
    }
    public void ImportRuleSet(string sourcePath)
    {
        if (!CanEditRuleSets) return;
        var imported = _ruleSetFiles.Import(sourcePath);
        UserRuleSets.Add(_ruleSetController.CreateUserRuleSet(imported.Name, imported.FileName));
        ShowMessage("Список добавлен. Сохраните настройки.");
    }
    public void RemoveSelectedRuleSet()
    {
        if (!CanRemoveUserRuleSet) return;
        var selected = SelectedUserRuleSet!;
        _pendingFileDeletes.Add(CloneRuleSet(selected));
        UserRuleSets.Remove(selected);
        SelectedUserRuleSet = null;
        ShowMessage("Список удалён. Сохраните настройки.");
    }
    public async Task DownloadSelectedBuiltinAsync()
    {
        if (!CanUseBuiltinRuleSet) return;
        var selected = SelectedBuiltinRuleSet!;
        SetRuleSetBusy(true);
        try
        {
            var result = await _ruleSetFiles.DownloadBuiltinAsync(selected, _ruleSetFiles.Exists(selected));
            if (!result.Success) { ShowError(result.Error ?? "Не удалось скачать список."); return; }
            var updated = CloneRuleSet(selected);
            updated.Enabled = true;
            updated.RemoteEtag = result.NotModified ? result.NewEtag ?? updated.RemoteEtag : result.NewEtag;
            updated.LastDownloadedUtc = DateTimeOffset.UtcNow;
            _pendingFileDeletes.RemoveAll(x => x.FileName == selected.FileName);
            BuiltinRuleSets[BuiltinRuleSets.IndexOf(selected)] = updated;
            SelectedBuiltinRuleSet = updated;
            ShowMessage(result.NotModified ? "Список уже актуален. Сохраните настройки." : "Список обновлён. Сохраните настройки.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetRuleSetBusy(false); }
    }
    public void RemoveSelectedBuiltin()
    {
        if (!CanUseBuiltinRuleSet) return;
        var selected = SelectedBuiltinRuleSet!;
        _pendingFileDeletes.Add(CloneRuleSet(selected));
        var updated = CloneRuleSet(selected);
        updated.Enabled = false;
        updated.RemoteEtag = null;
        updated.LastDownloadedUtc = null;
        BuiltinRuleSets[BuiltinRuleSets.IndexOf(selected)] = updated;
        SelectedBuiltinRuleSet = updated;
        ShowMessage("Список отключён. Файл будет удалён при сохранении.");
    }
    public string RuleSetCatalogUrl => _ruleSetFiles.CatalogUrl;
    public string ProxyOverride { get; set; } = "";
    public string InterfaceName { get; set; } = "";
    public string AddressCidr { get; set; } = "auto";
    public int Mtu { get => int.TryParse(MtuText, out var value) ? value : 0; set => MtuText = value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    public string MtuText { get; set; } = "1500";
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
            OnPropertyChanged(nameof(IsDohMode));
            UpdateDnsDetourAvailability();
        }
    }
    public bool IsDohMode => string.Equals(DnsMode, "doh", StringComparison.OrdinalIgnoreCase);
    public string DohServer { get => _dohServer; set { _dohServer = value; OnPropertyChanged(); UpdateDnsPreset(); } }
    public string DohPath { get => _dohPath; set { _dohPath = value; OnPropertyChanged(); UpdateDnsPreset(); } }
    public string DohSni { get => _dohSni; set { _dohSni = value; OnPropertyChanged(); UpdateDnsPreset(); } }
    public string DnsDetour { get; set; } = "direct";
    public string LogLevel { get; set; } = "warn";
    public string CloseBehavior { get; set; } = AppCloseBehavior.HideToTray;
    public bool IsDnsDetourEditable { get => _dnsDetourEditable; private set { if (_dnsDetourEditable == value) return; _dnsDetourEditable = value; OnPropertyChanged(); OnPropertyChanged(nameof(DnsDetourHint)); } }
    public string DnsDetourHint => !string.Equals(DnsMode, "doh", StringComparison.OrdinalIgnoreCase)
        ? "Маршрут используется только для защищённого DNS."
        : !DnsDetourPolicy.AllowsProxyDetour(_state.Mode)
            ? "В режиме TUN для выбранных приложений DNS всегда идёт напрямую."
            : string.Empty;
    public string ConnectionNotice => _isConnectionRunning
        ? "VPN подключён. Сетевые изменения будут применены после переподключения."
        : string.Empty;
    public void SetConnectionState(bool running)
    {
        if (_isConnectionRunning == running) return;
        _isConnectionRunning = running;
        OnPropertyChanged(nameof(ConnectionNotice));
    }
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
    public void Reload()
    {
        // Navigation refreshes connection context without discarding the editor draft.
        _state = _settingsService.GetState();
        if (!DnsDetourPolicy.AllowsProxyDetour(_state.Mode))
        {
            DnsDetour = "direct";
            OnPropertyChanged(nameof(DnsDetour));
        }
        UpdateDnsDetourAvailability();
    }
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
        if (!CanEditRuleSets) return;
        try
        {
            if (!int.TryParse(MtuText, out var mtu) || mtu < TunSettingsPolicy.MinMtu || mtu > TunSettingsPolicy.MaxMtu)
                throw new InvalidOperationException($"MTU должен быть целым числом от {TunSettingsPolicy.MinMtu} до {TunSettingsPolicy.MaxMtu}.");
            var paths = _tunAppsController.Normalize(TunApps.Select(x => x.Path)).ToArray();
            var rules = BuiltinRuleSets.Concat(UserRuleSets).Select(CloneRuleSet).ToArray();
            foreach (var removed in _pendingFileDeletes)
                foreach (var rule in rules.Where(x => x.FileName == removed.FileName)) rule.Enabled = false;
            _state=_settingsService.GetState();
            _controller.Save(_state, new ConnectionSettingsDraft(
                new ProxyConnectionSettings { ProxyOverride = ProxyOverride },
                new TunSettings { InterfaceName = InterfaceName, AddressCidr = AddressCidr, Mtu = mtu, Stack = Stack, AutoRoute = AutoRoute, StrictRoute = StrictRoute },
                new DnsSettings { Mode = DnsMode, DohServer = DohServer, DohPath = DohPath, DohSni = DohSni, Detour = DnsDetour })
            {
                TunAppPaths = paths, RuleSets = rules, LogLevel = LogLevel, CloseBehavior = CloseBehavior
            });
            var deleteErrors = new List<string>();
            foreach (var removed in _pendingFileDeletes.ToArray())
            {
                try { _ruleSetFiles.Delete(removed); _pendingFileDeletes.Remove(removed); }
                catch (Exception ex) { deleteErrors.Add(ex.Message); }
            }
            ApplyState(_settingsService.GetState());
            if (deleteErrors.Count > 0) ShowError("Настройки сохранены, но не удалось удалить файлы списков: " + string.Join("; ", deleteErrors));
            else ShowMessage(_isConnectionRunning
                ? "Настройки сохранены. Они применятся после переподключения."
                : "Все настройки сохранены.");
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    public void ShowError(string message)
    {
        _messageCancellation?.Cancel();
        Message = message;
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
        var items = paths.Select(TunAppListItem.FromPath).ToArray();
        SelectedTunApp = null;
        TunApps.Clear();
        foreach (var item in items) TunApps.Add(item);
    }
    private void ApplyState(AppStateModel state)
    {
        _state = state;
        ProxyOverride = state.ProxyOverride; InterfaceName = state.TunInterfaceName;
        AddressCidr = state.TunAddressCidr; Mtu = TunSettingsPolicy.NormalizeMtu(state.TunMtu);
        Stack = TunSettingsPolicy.ComboIndexToStack(TunSettingsPolicy.StackToComboIndex(state.TunStack)); AutoRoute = state.TunAutoRoute; StrictRoute = state.TunStrictRoute;
        DnsMode = state.DnsMode; DohServer = state.DohServer; DohPath = state.DohPath; DohSni = state.DohSni; DnsDetour = state.DnsDetour;
        LogLevel = state.SingBoxLogLevel; CloseBehavior = AppCloseBehavior.Normalize(state.CloseBehavior);
        UpdateDnsPreset();
        ReplaceTunApps(_tunAppsController.Normalize(state.TunAppProcessPaths));
        SelectedBuiltinRuleSet = null; SelectedUserRuleSet = null;
        BuiltinRuleSets.Clear(); UserRuleSets.Clear();
        var rules = _ruleSetController.Load(state);
        foreach (var item in rules.Builtin) BuiltinRuleSets.Add(CloneRuleSet(item));
        foreach (var item in rules.User) UserRuleSets.Add(CloneRuleSet(item));
        foreach (var property in new[] { nameof(ProxyOverride), nameof(InterfaceName), nameof(AddressCidr), nameof(Mtu), nameof(MtuText), nameof(Stack), nameof(AutoRoute), nameof(StrictRoute), nameof(DnsMode), nameof(DohServer), nameof(DohPath), nameof(DohSni), nameof(DnsDetour), nameof(LogLevel), nameof(CloseBehavior), nameof(DnsPreset) })
            OnPropertyChanged(property);
        UpdateDnsDetourAvailability();
    }
    private void UpdateDnsDetourAvailability()
    {
        IsDnsDetourEditable = string.Equals(DnsMode, "doh", StringComparison.OrdinalIgnoreCase) && DnsDetourPolicy.AllowsProxyDetour(_state.Mode);
        OnPropertyChanged(nameof(DnsDetourHint));
    }
    private void UpdateDnsPreset()
    {
        _dnsPreset = DnsPolicy.StateToPresetIndex(new DnsSettings { DohServer = DohServer, DohSni = DohSni, DohPath = DohPath }) switch
        {
            0 => "cloudflare", 1 => "google", 2 => "quad9", 3 => "adguard", _ => "custom"
        };
        OnPropertyChanged(nameof(DnsPreset));
    }
    private void SetRuleSetBusy(bool busy)
    {
        _isRuleSetBusy = busy;
        OnPropertyChanged(nameof(CanEditRuleSets));
        OnPropertyChanged(nameof(CanUseBuiltinRuleSet));
        OnPropertyChanged(nameof(CanRemoveUserRuleSet));
        ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
    }
    private static UserRuleSetModel CloneRuleSet(UserRuleSetModel item) => new()
    {
        Tag = item.Tag, Name = item.Name, FileName = item.FileName, Enabled = item.Enabled,
        Action = item.Action, BuiltinId = item.BuiltinId, RemoteEtag = item.RemoteEtag,
        LastDownloadedUtc = item.LastDownloadedUtc
    };
}

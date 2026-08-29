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
    private readonly IConnectionSettingsController _controller;
    private readonly AppStateModel _state;
    private readonly ITunAppsController _tunAppsController;
    private string? _message;
    public SettingsViewModel(IConnectionSettingsController controller, ITunAppsController tunAppsController, AppStateModel state)
    {
        _controller = controller; _tunAppsController = tunAppsController; _state = state;
        ProxyOverride = state.ProxyOverride; InterfaceName = state.TunInterfaceName;
        AddressCidr = state.TunAddressCidr; Mtu = TunSettingsPolicy.NormalizeMtu(state.TunMtu);
        Stack = TunSettingsPolicy.NormalizeStack(state.TunStack); AutoRoute = state.TunAutoRoute; StrictRoute = state.TunStrictRoute;
        DnsMode = state.DnsMode; DohServer = state.DohServer; DohPath = state.DohPath; DohSni = state.DohSni; DnsDetour = state.DnsDetour;
        SaveCommand = new RelayCommand(Save);
        foreach (var path in _tunAppsController.Normalize(state.TunAppProcessPaths)) TunApps.Add(path);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SaveCommand { get; }
    public ObservableCollection<string> TunApps { get; } = [];
    public string? SelectedTunApp { get; set; }
    public void AddTunApp(string path)
    {
        var saved = _tunAppsController.AddAndSave(_state, TunApps, [path]);
        TunApps.Clear(); foreach (var item in saved) TunApps.Add(item);
    }
    public void RemoveSelectedTunApp()
    {
        if (SelectedTunApp is null) return;
        var saved = _tunAppsController.RemoveAndSave(_state, TunApps, SelectedTunApp);
        TunApps.Clear(); foreach (var item in saved) TunApps.Add(item);
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
    public string? Message { get => _message; private set { _message = value; OnPropertyChanged(); } }
    private void Save()
    {
        try
        {
            _controller.Save(_state, new ConnectionSettingsDraft(
                new ProxyConnectionSettings { ProxyOverride = ProxyOverride },
                new TunSettings { InterfaceName = InterfaceName, AddressCidr = AddressCidr, Mtu = Mtu, Stack = Stack, AutoRoute = AutoRoute, StrictRoute = StrictRoute },
                new DnsSettings { Mode = DnsMode, DohServer = DohServer, DohPath = DohPath, DohSni = DohSni, Detour = DnsDetour }));
            Message = "Настройки сохранены.";
        }
        catch (Exception ex) { Message = ex.Message; }
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

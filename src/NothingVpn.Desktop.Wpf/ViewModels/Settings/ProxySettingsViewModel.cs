using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Desktop.Wpf;

public sealed class ProxySettingsViewModel : SettingsSection
{
    public string ProxyOverride { get; set; } = "";

    internal void Load(AppStateModel state)
    {
        ProxyOverride = state.ProxyOverride;
        OnPropertyChanged(nameof(ProxyOverride));
    }

    internal ProxyConnectionSettings ToSettings() => new() { ProxyOverride = ProxyOverride };
}

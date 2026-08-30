using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Services;

public interface ISettingsService
{
    event EventHandler<AppStateModel>? StateChanged;
    AppStateModel GetState();
    void SaveState(AppStateModel state);
    void UpdateState(Action<AppStateModel> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var state = GetState();
        update(state);
        SaveState(state);
    }
    void UpdateMode(string mode);
    void UpdateDns(string mode, string dohServer, string dohPath, string dohSni, string detour);
    void UpdateTunSettings(TunSettings settings);
    void UpdateProxySettings(ProxyConnectionSettings settings);
    void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets);
    void UpdateTunApps(IReadOnlyCollection<string> paths);
}


using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface ISettingsService
{
    event EventHandler<AppStateModel>? StateChanged;
    AppStateModel GetState();
    void SaveState(AppStateModel state);
    void UpdateMode(string mode);
    void UpdateDns(string mode, string dohServer, string dohPath, string dohSni, string detour);
    void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets);
    void UpdateTunApps(IReadOnlyCollection<string> paths);
}


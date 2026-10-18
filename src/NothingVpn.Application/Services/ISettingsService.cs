using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Services;

public interface ISettingsService
{
    event EventHandler<AppStateModel>? StateChanged;
    AppStateModel GetState();

    /// <summary>
    /// Атомарно меняет поля актуального состояния под локом стора. Runtime-флаги подключения
    /// (<see cref="ConnectionRuntimeState"/>) через этот метод изменить нельзя — они сохраняются как есть.
    /// </summary>
    void UpdateState(Action<AppStateModel> update);
    void UpdateMode(string mode);
    void UpdateDns(string mode, string dohServer, string dohPath, string dohSni, string detour);
    void UpdateTunSettings(TunSettings settings);
    void UpdateProxySettings(ProxyConnectionSettings settings);
    void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets);
    void UpdateTunApps(IReadOnlyCollection<string> paths);
}


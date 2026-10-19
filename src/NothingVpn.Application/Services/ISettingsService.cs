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
    void UpdateMode(ConnectionMode mode);
    void UpdateDns(DnsMode mode, string dohServer, string dohPath, string dohSni, DnsDetour detour);
    void UpdateTunSettings(TunSettings settings);
    void UpdateProxySettings(ProxyConnectionSettings settings);
    void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets);
    void UpdateTunApps(IReadOnlyCollection<string> paths);
}


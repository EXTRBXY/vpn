using NothingVpn.Application.Mappers;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public sealed class SettingsService(AppStateStore stateStore, IPathPolicyPort pathPolicy) : ISettingsService
{
    public event EventHandler<AppStateModel>? StateChanged;

    public AppStateModel GetState() => stateStore.Load();

    public void UpdateState(Action<AppStateModel> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var state = stateStore.Update(current =>
        {
            var runtime = ConnectionRuntimeState.From(current);
            update(current);
            AppStateNormalizer.Normalize(current, pathPolicy);
            runtime.ApplyTo(current);
        });
        StateChanged?.Invoke(this, state);
    }

    public void UpdateMode(string mode)
    {
        UpdateState(state => state.Mode = ConnectionPolicy.NormalizeMode(mode));
    }

    public void UpdateDns(string mode, string dohServer, string dohPath, string dohSni, string detour)
    {
        var dns = new DnsSettings
        {
            Mode = mode,
            DohServer = dohServer,
            DohPath = dohPath,
            DohSni = dohSni,
            Detour = detour
        };
        DnsPolicy.Normalize(dns);
        DnsPolicy.Validate(dns);
        UpdateState(state => ConnectionSettingsMapper.ApplyDnsSettings(state, dns));
    }

    public void UpdateTunSettings(TunSettings settings)
    {
        TunSettingsPolicy.Normalize(settings);
        UpdateState(state => ConnectionSettingsMapper.ApplyTunSettings(state, settings));
    }

    public void UpdateProxySettings(ProxyConnectionSettings settings)
    {
        ProxyConnectionPolicy.Normalize(settings);
        UpdateState(state => ConnectionSettingsMapper.ApplyProxySettings(state, settings));
    }

    public void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets)
    {
        UpdateState(state => state.UserRuleSets = ruleSets.ToList());
    }

    public void UpdateTunApps(IReadOnlyCollection<string> paths)
    {
        UpdateState(state => state.TunAppProcessPaths = pathPolicy.NormalizeDistinctExePaths(paths).ToList());
    }
}

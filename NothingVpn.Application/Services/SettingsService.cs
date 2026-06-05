using NothingVpn.Application.Mappers;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public sealed class SettingsService(IStateStorePort stateStore, IPathPolicyPort pathPolicy) : ISettingsService
{
    public event EventHandler<AppStateModel>? StateChanged;

    public AppStateModel GetState() => stateStore.Load();

    public void SaveState(AppStateModel state)
    {
        state.Mode = ConnectionPolicy.NormalizeMode(state.Mode);
        state.TunAppProcessPaths = pathPolicy.NormalizeDistinctExePaths(state.TunAppProcessPaths).ToList();
        NormalizeConnectionSettings(state);
        stateStore.Save(state);
        StateChanged?.Invoke(this, state);
    }

    public void UpdateMode(string mode)
    {
        var state = stateStore.Load();
        state.Mode = ConnectionPolicy.NormalizeMode(mode);
        SaveState(state);
    }

    public void UpdateDns(string mode, string dohServer, string dohPath, string dohSni, string detour)
    {
        var state = stateStore.Load();
        var dns = new DnsSettings
        {
            Mode = mode,
            DohServer = dohServer,
            DohPath = dohPath,
            DohSni = dohSni,
            Detour = detour
        };
        DnsPolicy.Normalize(dns);
        ConnectionSettingsMapper.ApplyDnsSettings(state, dns);
        SaveState(state);
    }

    public void UpdateTunSettings(TunSettings settings)
    {
        var state = stateStore.Load();
        TunSettingsPolicy.Normalize(settings);
        ConnectionSettingsMapper.ApplyTunSettings(state, settings);
        SaveState(state);
    }

    public void UpdateProxySettings(ProxyConnectionSettings settings)
    {
        var state = stateStore.Load();
        ProxyConnectionPolicy.Normalize(settings);
        ConnectionSettingsMapper.ApplyProxySettings(state, settings);
        SaveState(state);
    }

    public void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets)
    {
        var state = stateStore.Load();
        state.UserRuleSets = ruleSets.ToList();
        SaveState(state);
    }

    public void UpdateTunApps(IReadOnlyCollection<string> paths)
    {
        var state = stateStore.Load();
        state.TunAppProcessPaths = pathPolicy.NormalizeDistinctExePaths(paths).ToList();
        SaveState(state);
    }

    private static void NormalizeConnectionSettings(AppStateModel state)
    {
        var dns = ConnectionSettingsMapper.ToDnsSettings(state);
        DnsPolicy.Normalize(dns);
        ConnectionSettingsMapper.ApplyDnsSettings(state, dns);

        var tun = ConnectionSettingsMapper.ToTunSettings(state);
        TunSettingsPolicy.Normalize(tun);
        ConnectionSettingsMapper.ApplyTunSettings(state, tun);

        var proxy = ConnectionSettingsMapper.ToProxySettings(state);
        ProxyConnectionPolicy.Normalize(proxy);
        ConnectionSettingsMapper.ApplyProxySettings(state, proxy);
    }
}


using NothingVpn.Application.Mappers;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public sealed class SettingsService(IStateStorePort stateStore, IPathPolicyPort pathPolicy) : ISettingsService
{
    private readonly object _stateSync = new();
    public event EventHandler<AppStateModel>? StateChanged;

    public AppStateModel GetState()
    {
        lock (_stateSync) return stateStore.Load();
    }

    public void SaveState(AppStateModel state)
    {
        lock (_stateSync)
        {
            NormalizeState(state);
            stateStore.Save(state);
        }
        StateChanged?.Invoke(this, state);
    }

    public void UpdateState(Action<AppStateModel> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        AppStateModel state;
        lock (_stateSync)
        {
            state = stateStore.Load();
            update(state);
            NormalizeState(state);
            stateStore.Save(state);
        }
        StateChanged?.Invoke(this, state);
    }

    private void NormalizeState(AppStateModel state)
    {
        state.Mode = ConnectionPolicy.NormalizeMode(state.Mode);
        state.DnsDetour = DnsDetourPolicy.EffectiveDetour(state.Mode, state.DnsDetour);
        state.CloseBehavior = AppCloseBehavior.Normalize(state.CloseBehavior);
        state.TunAppProcessPaths = pathPolicy.NormalizeDistinctExePaths(state.TunAppProcessPaths).ToList();
        NormalizeConnectionSettings(state);
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


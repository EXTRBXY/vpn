using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Mappers;

public static class ConnectionSettingsMapper
{
    public static DnsSettings ToDnsSettings(AppStateModel state) => new()
    {
        Mode = state.DnsMode,
        DohServer = state.DohServer,
        DohPath = state.DohPath,
        DohSni = state.DohSni,
        Detour = state.DnsDetour
    };

    public static void ApplyDnsSettings(AppStateModel state, DnsSettings dns)
    {
        state.DnsMode = dns.Mode;
        state.DohServer = dns.DohServer;
        state.DohPath = dns.DohPath;
        state.DohSni = dns.DohSni;
        state.DnsDetour = dns.Detour;
    }

    public static TunSettings ToTunSettings(AppStateModel state) => new()
    {
        InterfaceName = state.TunInterfaceName,
        AddressCidr = state.TunAddressCidr,
        Mtu = state.TunMtu,
        Stack = state.TunStack,
        AutoRoute = state.TunAutoRoute,
        StrictRoute = state.TunStrictRoute
    };

    public static void ApplyTunSettings(AppStateModel state, TunSettings tun)
    {
        state.TunInterfaceName = tun.InterfaceName;
        state.TunAddressCidr = tun.AddressCidr;
        state.TunMtu = tun.Mtu;
        state.TunStack = tun.Stack;
        state.TunAutoRoute = tun.AutoRoute;
        state.TunStrictRoute = tun.StrictRoute;
    }

    public static IEnumerable<UserRuleSetEntry> ToRuleSetEntries(IEnumerable<UserRuleSetModel> ruleSets) =>
        ruleSets.Select(x => new UserRuleSetEntry
        {
            Tag = x.Tag,
            Name = x.Name,
            FileName = x.FileName,
            Enabled = x.Enabled,
            Action = x.Action
        });

    public static XmuxSettings ToXmuxSettings(AppStateModel state) => new()
    {
        MaxConcurrency = state.XhttpXmuxMaxConcurrency,
        MaxConnections = state.XhttpXmuxMaxConnections,
        CMaxReuseTimes = state.XhttpXmuxCMaxReuseTimes,
        HMaxRequestTimes = state.XhttpXmuxHMaxRequestTimes,
        HMaxReusableSecs = state.XhttpXmuxHMaxReusableSecs
    };

    public static void ApplyXmuxSettings(AppStateModel state, XmuxSettings xmux)
    {
        state.XhttpXmuxMaxConcurrency = xmux.MaxConcurrency;
        state.XhttpXmuxMaxConnections = xmux.MaxConnections;
        state.XhttpXmuxCMaxReuseTimes = xmux.CMaxReuseTimes;
        state.XhttpXmuxHMaxRequestTimes = xmux.HMaxRequestTimes;
        state.XhttpXmuxHMaxReusableSecs = xmux.HMaxReusableSecs;
    }

    public static ProxyConnectionSettings ToProxySettings(AppStateModel state) => new()
    {
        ProxyOverride = state.ProxyOverride
    };

    public static void ApplyProxySettings(AppStateModel state, ProxyConnectionSettings proxy)
    {
        state.ProxyOverride = proxy.ProxyOverride;
    }
}

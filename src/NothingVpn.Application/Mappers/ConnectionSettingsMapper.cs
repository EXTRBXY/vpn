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

    public static ProxyConnectionSettings ToProxySettings(AppStateModel state) => new()
    {
        ProxyOverride = state.ProxyOverride
    };

    public static void ApplyProxySettings(AppStateModel state, ProxyConnectionSettings proxy)
    {
        state.ProxyOverride = proxy.ProxyOverride;
    }
}

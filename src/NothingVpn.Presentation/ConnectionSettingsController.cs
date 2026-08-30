using NothingVpn.Application.Mappers;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Presentation;

public sealed class ConnectionSettingsController : IConnectionSettingsController
{
    private readonly ISettingsService _settingsService;

    public ConnectionSettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Save(AppStateModel state, ConnectionSettingsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(draft);

        var proxy = Clone(draft.Proxy);
        var tun = Clone(draft.Tun);
        var dns = Clone(draft.Dns);

        ProxyConnectionPolicy.Validate(proxy);
        TunSettingsPolicy.Validate(tun);
        dns.Detour = DnsDetourPolicy.EffectiveDetour(state.Mode, dns.Detour);
        if (string.Equals(dns.Mode?.Trim(), "doh", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(dns.DohServer))
                throw new InvalidOperationException("DoH IP не задан.");
            if (string.IsNullOrWhiteSpace(dns.DohSni))
                throw new InvalidOperationException("DoH SNI не задан (нужен для TLS).");
        }
        DnsPolicy.Validate(dns);

        ConnectionSettingsMapper.ApplyProxySettings(state, proxy);
        ConnectionSettingsMapper.ApplyTunSettings(state, tun);
        ConnectionSettingsMapper.ApplyDnsSettings(state, dns);
        _settingsService.SaveState(state);
    }

    private static ProxyConnectionSettings Clone(ProxyConnectionSettings source) => new()
    {
        ProxyOverride = source.ProxyOverride
    };

    private static TunSettings Clone(TunSettings source) => new()
    {
        InterfaceName = source.InterfaceName,
        AddressCidr = source.AddressCidr,
        Mtu = source.Mtu,
        Stack = source.Stack,
        AutoRoute = source.AutoRoute,
        StrictRoute = source.StrictRoute
    };

    private static DnsSettings Clone(DnsSettings source) => new()
    {
        Mode = source.Mode,
        DohServer = source.DohServer,
        DohPath = source.DohPath,
        DohSni = source.DohSni,
        Detour = source.Detour
    };
}

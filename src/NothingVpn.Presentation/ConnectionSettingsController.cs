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
        if (string.Equals(dns.Mode?.Trim(), "doh", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(dns.DohServer))
                throw new InvalidOperationException("DoH IP не задан.");
            if (string.IsNullOrWhiteSpace(dns.DohSni))
                throw new InvalidOperationException("DoH SNI не задан (нужен для TLS).");
        }
        DnsPolicy.Validate(dns);

        // Merge only the edited fields into the latest state under the settings lock.
        // Background updates and connection recovery data must survive a UI save.
        _settingsService.UpdateState(current =>
        {
            dns.Detour = DnsDetourPolicy.EffectiveDetour(current.Mode, dns.Detour);
            Apply(current);
        });
        Apply(state);

        void Apply(AppStateModel target)
        {
            ConnectionSettingsMapper.ApplyProxySettings(target, proxy);
            ConnectionSettingsMapper.ApplyTunSettings(target, tun);
            ConnectionSettingsMapper.ApplyDnsSettings(target, dns);
            if (draft.TunAppPaths is not null) target.TunAppProcessPaths = draft.TunAppPaths.ToList();
            if (draft.RuleSets is not null) target.UserRuleSets = draft.RuleSets.ToList();
            if (draft.LogLevel is not null) target.SingBoxLogLevel = draft.LogLevel;
            if (draft.CloseBehavior is not null) target.CloseBehavior = AppCloseBehavior.Normalize(draft.CloseBehavior);
        }
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

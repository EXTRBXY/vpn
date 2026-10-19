using NothingVpn.Domain.Models;

namespace NothingVpn.Tray.Internal.UI;

/// <summary>Порядок пунктов в комбобоксах WinForms-клиента (перенесено из Domain-политик).</summary>
internal static class ComboIndexes
{
    public static int FromMode(ConnectionMode mode) => mode switch
    {
        ConnectionMode.Tun => 1,
        ConnectionMode.TunApps => 2,
        _ => 0
    };

    public static ConnectionMode ToMode(int index) => index switch
    {
        1 => ConnectionMode.Tun,
        2 => ConnectionMode.TunApps,
        _ => ConnectionMode.Proxy
    };

    public static int FromStack(TunStack stack) => stack switch
    {
        TunStack.Mixed => 1,
        TunStack.Gvisor => 2,
        _ => 0
    };

    public static TunStack ToStack(int index) => index switch
    {
        1 => TunStack.Mixed,
        2 => TunStack.Gvisor,
        _ => TunStack.Default
    };

    public static int FromDnsMode(DnsMode mode) => mode == DnsMode.System ? 0 : 1;

    public static DnsMode ToDnsMode(int index) => index == 0 ? DnsMode.System : DnsMode.Doh;

    public static int FromDetour(DnsDetour detour) => detour == DnsDetour.Proxy ? 1 : 0;

    public static DnsDetour ToDetour(int index) => index == 1 ? DnsDetour.Proxy : DnsDetour.Direct;

    public static int FromPreset(DnsPreset preset) => preset switch
    {
        DnsPreset.Cloudflare => 0,
        DnsPreset.Google => 1,
        DnsPreset.Quad9 => 2,
        DnsPreset.AdGuard => 3,
        _ => 4
    };

    public static DnsPreset ToPreset(int index) => index switch
    {
        0 => DnsPreset.Cloudflare,
        1 => DnsPreset.Google,
        2 => DnsPreset.Quad9,
        3 => DnsPreset.AdGuard,
        _ => DnsPreset.Custom
    };
}

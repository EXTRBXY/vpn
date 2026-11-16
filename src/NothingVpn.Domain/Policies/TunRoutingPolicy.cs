using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class TunRoutingPolicy
{
    private static readonly string[] KnownSecureDnsDomainsArray =
    [
        "dns.google",
        "dns.google.com",
        "cloudflare-dns.com",
        "security.cloudflare-dns.com",
        "chrome.cloudflare-dns.com",
        "mozilla.cloudflare-dns.com",
        "dns.quad9.net",
        "dns11.quad9.net",
        "he.dns.opendns.com",
        "doh.opendns.com",
        "dns.nextdns.io",
        "dns.adguard.com",
        "unfiltered.adguard-dns.com",
        "family.adguard-dns.com"
    ];

    public static IReadOnlyList<string> KnownSecureDnsDomains { get; } = KnownSecureDnsDomainsArray;

    /// <summary>
    /// DNS hijack нужен во всех TUN-режимах: на Windows DNS часто идёт от svchost,
    /// а не от целевого .exe — без hijack process_path не спасает от отравления DNS.
    /// </summary>
    public static bool HijackDns(ConnectionMode mode) =>
        ConnectionPolicy.IsTunMode(mode);

    public static bool RouteIpv6ThroughProxy(ConnectionMode mode) =>
        mode == ConnectionMode.Tun;

    /// <summary>
    /// В полном TUN QUIC/HTTP3 уводим в proxy (иначе часто обходит правила).
    /// В tun_apps — нет: иначе весь QUIC системы уйдёт в VPN до process_path.
    /// </summary>
    public static bool RouteQuicThroughProxy(ConnectionMode mode) =>
        mode == ConnectionMode.Tun;

    /// <summary>
    /// Принудительный увод известных DoH-доменов в proxy только в полном TUN.
    /// В tun_apps DoH dial остаётся direct (DnsDetour запрещён), а TCP приложений
    /// идёт через process_path.
    /// </summary>
    public static bool RouteSecureDnsThroughProxy(ConnectionMode mode) =>
        mode == ConnectionMode.Tun;

    /// <summary>
    /// На Windows в sing-box 1.13 трафик к собственным префиксам TUN (шлюз / DNS .2)
    /// при <c>ip_is_private → direct</c> зацикливается обратно в тот же TUN
    /// (<see href="https://github.com/SagerNet/sing-box/issues/4455">#4455</see>).
    /// Reject/drop этих CIDR — после hijack-dns и до private→direct.
    /// </summary>
    public static bool RejectTunSelfPrefixes(ConnectionMode mode) =>
        ConnectionPolicy.IsTunMode(mode);
}

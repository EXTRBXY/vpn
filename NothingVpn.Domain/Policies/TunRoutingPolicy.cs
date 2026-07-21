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
    public static bool HijackDns(string? mode) =>
        ConnectionPolicy.IsTunMode(ConnectionPolicy.NormalizeMode(mode));

    public static bool RouteIpv6ThroughProxy(string? mode) =>
        string.Equals(ConnectionPolicy.NormalizeMode(mode), ConnectionPolicy.TunMode, StringComparison.Ordinal);

    /// <summary>
    /// В полном TUN QUIC/HTTP3 уводим в proxy (иначе часто обходит правила).
    /// В tun_apps — нет: иначе весь QUIC системы уйдёт в VPN до process_path.
    /// </summary>
    public static bool RouteQuicThroughProxy(string? mode) =>
        string.Equals(ConnectionPolicy.NormalizeMode(mode), ConnectionPolicy.TunMode, StringComparison.Ordinal);

    /// <summary>
    /// Принудительный увод известных DoH-доменов в proxy только в полном TUN.
    /// В tun_apps DoH dial остаётся direct (DnsDetour запрещён), а TCP приложений
    /// идёт через process_path.
    /// </summary>
    public static bool RouteSecureDnsThroughProxy(string? mode) =>
        string.Equals(ConnectionPolicy.NormalizeMode(mode), ConnectionPolicy.TunMode, StringComparison.Ordinal);
}

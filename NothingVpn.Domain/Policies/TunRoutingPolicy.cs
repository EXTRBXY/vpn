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

    public static bool HijackDns(string? mode) =>
        string.Equals(ConnectionPolicy.NormalizeMode(mode), ConnectionPolicy.TunMode, StringComparison.Ordinal);

    public static bool RouteIpv6ThroughProxy(string? mode) =>
        string.Equals(ConnectionPolicy.NormalizeMode(mode), ConnectionPolicy.TunMode, StringComparison.Ordinal);

    public static bool RouteQuicThroughProxy(string? mode) =>
        ConnectionPolicy.IsTunMode(ConnectionPolicy.NormalizeMode(mode));
}

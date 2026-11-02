using System.Net;
using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class TunBootstrapPolicy
{
    public const string BootstrapLocalDnsTag = "bootstrap-local";

    public static IReadOnlyList<string> CollectEndpointDomains(string? host)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDomain(set, host);
        return set.ToList();
    }

    /// <summary>Адрес сервера как CIDR одного хоста, если он задан IP-адресом; для домена — null.</summary>
    public static string? EndpointHostCidr(string? host)
    {
        var value = (host ?? string.Empty).Trim().TrimStart('[').TrimEnd(']');
        if (!IPAddress.TryParse(value, out var ip))
            return null;
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"{ip}/128" : $"{ip}/32";
    }

    public static string? ResolveDefaultDomainResolver(bool useTun, bool useDohResolver)
    {
        if (!useTun)
            return null;

        return BootstrapLocalDnsTag;
    }

    /// <summary>Detour DoH-сервера в конфиге sing-box: тег outbound <c>proxy</c> или null (напрямую).</summary>
    public static string? ResolveSingBoxDohDetour(ConnectionMode connectionMode, bool tunStrictRoute, DnsDetour userDetour)
    {
        if (!ConnectionPolicy.IsTunMode(connectionMode))
            return null;

        // tun_apps: DNS через proxy запрещён (модель split-tunnel + DnsDetourPolicy).
        if (TunAppsPolicy.IsTunApps(connectionMode))
            return null;

        var effectiveStrictRoute = TunAppsPolicy.UseStrictRoute(connectionMode, tunStrictRoute);
        if (effectiveStrictRoute || userDetour == DnsDetour.Proxy)
            return DnsDetour.Proxy.ToValue();

        return null;
    }

    private static void AddDomain(ISet<string> set, string? value)
    {
        var domain = (value ?? string.Empty).Trim();
        if (domain.Length == 0 || IPAddress.TryParse(domain, out _))
            return;
        set.Add(domain);
    }
}

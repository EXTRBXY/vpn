using System.Net;

namespace NothingVpn.Domain.Policies;

public static class TunBootstrapPolicy
{
    public const string BootstrapLocalDnsTag = "bootstrap-local";
    public const string LocalDnsTag = "local";

    public static IReadOnlyList<string> CollectEndpointDomains(string? host, string? sni)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDomain(set, host);
        AddDomain(set, sni);
        return set.ToList();
    }

    public static string? ResolveDefaultDomainResolver(bool useTun, bool useDohResolver)
    {
        if (!useTun)
            return null;
        return useDohResolver ? BootstrapLocalDnsTag : LocalDnsTag;
    }

    public static string? ResolveSingBoxDohDetour(string? connectionMode, bool tunStrictRoute, string? userDetour)
    {
        var mode = ConnectionPolicy.NormalizeMode(connectionMode);
        if (!ConnectionPolicy.IsTunMode(mode))
            return null;

        var detour = NormalizeDetour(userDetour);
        if (tunStrictRoute && detour != "proxy")
            return "proxy";

        return detour == "proxy" ? "proxy" : null;
    }

    private static void AddDomain(ISet<string> set, string? value)
    {
        var domain = (value ?? string.Empty).Trim();
        if (domain.Length == 0 || IPAddress.TryParse(domain, out _))
            return;
        set.Add(domain);
    }

    private static string NormalizeDetour(string? detour)
    {
        var normalized = (detour ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "proxy" ? "proxy" : "direct";
    }
}

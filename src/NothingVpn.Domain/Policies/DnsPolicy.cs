using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class DnsPolicy
{
    public static void Normalize(DnsSettings settings)
    {
        settings.Mode = NormalizeMode(settings.Mode);
        settings.Detour = NormalizeDetour(settings.Detour);
        settings.DohPath = NormalizePath(settings.DohPath);
        settings.DohServer = (settings.DohServer ?? string.Empty).Trim();
        settings.DohSni = (settings.DohSni ?? string.Empty).Trim();
    }

    public static void Validate(DnsSettings settings)
    {
        Normalize(settings);
        if (settings.Mode != "doh")
            return;

        if (string.IsNullOrWhiteSpace(settings.DohServer))
            throw new InvalidOperationException("DoH IP не задан.");
        if (string.IsNullOrWhiteSpace(settings.DohSni))
            throw new InvalidOperationException("DoH SNI не задан.");
    }

    public static int StateToPresetIndex(DnsSettings settings)
    {
        var server = (settings.DohServer ?? string.Empty).Trim();
        var sni = (settings.DohSni ?? string.Empty).Trim();
        var path = NormalizePath(settings.DohPath);
        if (!string.Equals(path, "/dns-query", StringComparison.Ordinal)) return 4;
        if (server == "1.1.1.1" && sni.Equals("cloudflare-dns.com", StringComparison.OrdinalIgnoreCase)) return 0;
        if (server == "8.8.8.8" && sni.Equals("dns.google", StringComparison.OrdinalIgnoreCase)) return 1;
        if (server == "9.9.9.9" && sni.Equals("dns.quad9.net", StringComparison.OrdinalIgnoreCase)) return 2;
        if (server == "94.140.14.14" && sni.Equals("dns.adguard.com", StringComparison.OrdinalIgnoreCase)) return 3;
        return 4;
    }

    public static bool IsDohMode(DnsSettings settings) =>
        string.Equals(NormalizeMode(settings.Mode), "doh", StringComparison.Ordinal);

    public static int DetourToComboIndex(string? detour) =>
        string.Equals(NormalizeDetour(detour), "proxy", StringComparison.Ordinal) ? 1 : 0;

    public static string ComboIndexToDetour(int index) =>
        index == 1 ? "proxy" : "direct";

    public static string DetourToDisplayLabel(string? detour) =>
        string.Equals(NormalizeDetour(detour), "proxy", StringComparison.Ordinal)
            ? "Через VPN"
            : "Напрямую";

    public static DnsSettings ApplyPreset(int presetIndex, DnsSettings settings)
    {
        switch (presetIndex)
        {
            case 0:
                settings.DohServer = "1.1.1.1";
                settings.DohSni = "cloudflare-dns.com";
                settings.DohPath = "/dns-query";
                break;
            case 1:
                settings.DohServer = "8.8.8.8";
                settings.DohSni = "dns.google";
                settings.DohPath = "/dns-query";
                break;
            case 2:
                settings.DohServer = "9.9.9.9";
                settings.DohSni = "dns.quad9.net";
                settings.DohPath = "/dns-query";
                break;
            case 3:
                settings.DohServer = "94.140.14.14";
                settings.DohSni = "dns.adguard.com";
                settings.DohPath = "/dns-query";
                break;
        }

        Normalize(settings);
        return settings;
    }

    private static string NormalizeMode(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "system" or "doh" ? normalized : "doh";
    }

    private static string NormalizeDetour(string? detour)
    {
        var normalized = (detour ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "proxy" ? "proxy" : "direct";
    }

    private static string NormalizePath(string? path)
    {
        var normalized = (path ?? string.Empty).Trim();
        return normalized.Length == 0 ? "/dns-query" : normalized;
    }
}

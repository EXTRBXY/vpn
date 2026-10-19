using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class DnsPolicy
{
    public const DnsMode DefaultMode = DnsMode.Doh;
    public const string DefaultDohServer = "8.8.8.8";
    public const string DefaultDohPath = "/dns-query";
    public const string DefaultDohSni = "dns.google";
    public const DnsDetour DefaultDetour = DnsDetour.Direct;

    public static readonly SettingValueMap<DnsMode> Modes = new(
        DefaultMode,
        new Dictionary<DnsMode, string>
        {
            [DnsMode.Doh] = "doh",
            [DnsMode.System] = "system"
        });

    public static readonly SettingValueMap<DnsDetour> Detours = new(
        DefaultDetour,
        new Dictionary<DnsDetour, string>
        {
            [DnsDetour.Direct] = "direct",
            [DnsDetour.Proxy] = "proxy"
        });

    private static readonly (DnsPreset Preset, string Server, string Sni)[] Presets =
    [
        (DnsPreset.Cloudflare, "1.1.1.1", "cloudflare-dns.com"),
        (DnsPreset.Google, "8.8.8.8", "dns.google"),
        (DnsPreset.Quad9, "9.9.9.9", "dns.quad9.net"),
        (DnsPreset.AdGuard, "94.140.14.14", "dns.adguard.com")
    ];

    public static void Normalize(DnsSettings settings)
    {
        settings.Mode = Modes.Normalize(settings.Mode);
        settings.Detour = Detours.Normalize(settings.Detour);
        settings.DohPath = NormalizePath(settings.DohPath);
        settings.DohServer = (settings.DohServer ?? string.Empty).Trim();
        settings.DohSni = (settings.DohSni ?? string.Empty).Trim();
    }

    public static void Validate(DnsSettings settings)
    {
        Normalize(settings);
        if (settings.Mode != DnsMode.Doh)
            return;

        if (string.IsNullOrWhiteSpace(settings.DohServer))
            throw new InvalidOperationException("DoH IP не задан.");
        if (string.IsNullOrWhiteSpace(settings.DohSni))
            throw new InvalidOperationException("DoH SNI не задан.");
    }

    public static DnsPreset DetectPreset(DnsSettings settings)
    {
        var server = (settings.DohServer ?? string.Empty).Trim();
        var sni = (settings.DohSni ?? string.Empty).Trim();
        if (!string.Equals(NormalizePath(settings.DohPath), DefaultDohPath, StringComparison.Ordinal))
            return DnsPreset.Custom;

        foreach (var preset in Presets)
        {
            if (server == preset.Server && sni.Equals(preset.Sni, StringComparison.OrdinalIgnoreCase))
                return preset.Preset;
        }

        return DnsPreset.Custom;
    }

    /// <summary>Подставляет параметры набора; <see cref="DnsPreset.Custom"/> оставляет их как есть.</summary>
    public static DnsSettings ApplyPreset(DnsPreset preset, DnsSettings settings)
    {
        foreach (var known in Presets)
        {
            if (known.Preset != preset)
                continue;

            settings.DohServer = known.Server;
            settings.DohSni = known.Sni;
            settings.DohPath = DefaultDohPath;
        }

        Normalize(settings);
        return settings;
    }

    private static string NormalizePath(string? path)
    {
        var normalized = (path ?? string.Empty).Trim();
        return normalized.Length == 0 ? DefaultDohPath : normalized;
    }
}

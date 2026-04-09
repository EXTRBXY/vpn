using NothingVpn.Tray.Internal.WinInet;

namespace NothingVpn.Tray.Internal.Store;

internal sealed class AppState
{
    public string? ActiveProfileId { get; set; }

    // proxy | tun (весь трафик через proxy outbound) | tun_apps (только выбранные процессы)
    public string Mode { get; set; } = "proxy";

    /// <summary>Полные пути .exe для режима <see cref="Mode"/> = tun_apps (sing-box process_path).</summary>
    public List<string> TunAppProcessPaths { get; set; } = new();

    /// <summary>
    /// Пользовательские rule-set (sing-box) в бинарном формате .srs.
    /// Применяются в режимах Proxy и TUN как приоритетные правила (direct/block).
    /// </summary>
    public List<UserRuleSet> UserRuleSets { get; set; } = new();

    public static bool IsTunMode(string? mode)
    {
        var m = (mode ?? "").Trim().ToLowerInvariant();
        return m is "tun" or "tun_apps";
    }

    public int LocalMixedPort { get; set; } = 1080;

    public bool ProxyWasEnabledByUs { get; set; } = false;

    public WinInetProxySettingsSnapshot? PreviousProxySettings { get; set; }

    public string ProxyOverride { get; set; } = "localhost;127.*;10.*;192.168.*;172.16.*";

    // TUN settings (MVP defaults)
    public string TunInterfaceName { get; set; } = "NothingVpn";
    // "auto" means derive a unique /30 per profile to avoid collisions.
    public string TunAddressCidr { get; set; } = "auto";
    public int TunMtu { get; set; } = 9000;
    // system|gvisor|mixed (sing-box tun stack)
    public string TunStack { get; set; } = "system";
    public bool TunAutoRoute { get; set; } = true;
    public bool TunStrictRoute { get; set; } = true;

    // DNS settings (MVP defaults: Cloudflare DoH)
    // dns_mode: system|doh
    public string DnsMode { get; set; } = "doh";
    public string DohServer { get; set; } = "1.1.1.1";
    public string DohPath { get; set; } = "/dns-query";
    public string DohSni { get; set; } = "cloudflare-dns.com";

    /// <summary>
    /// Detour для DNS-запросов (Dial Fields): direct|proxy.
    /// Нужен для корректного bootstrap в некоторых сетях.
    /// </summary>
    public string DnsDetour { get; set; } = "direct";

    public bool DebugLogs { get; set; } = false;

    // sing-box log level: trace|debug|info|warn|error|fatal|panic
    public string SingBoxLogLevel { get; set; } = "warn";

    // If set, we verify sing-box.exe SHA-256 matches before starting.
    public string? TrustedSingBoxSha256 { get; set; }
}

internal sealed class UserRuleSet
{
    /// <summary>Стабильный tag для sing-box route.rule_set и matcher rule_set.</summary>
    public string Tag { get; set; } = "";

    /// <summary>Имя для UI.</summary>
    public string Name { get; set; } = "";

    /// <summary>Имя файла внутри каталога RuleSetsDir (обычно *.srs).</summary>
    public string FileName { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Действие по доменам из rule-set: direct|block.</summary>
    public string Action { get; set; } = "direct";
}


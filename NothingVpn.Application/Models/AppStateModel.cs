namespace NothingVpn.Application.Models;

public sealed class AppStateModel
{
    public string? ActiveProfileId { get; set; }
    public string Mode { get; set; } = "proxy";
    public List<string> TunAppProcessPaths { get; set; } = new();
    public List<UserRuleSetModel> UserRuleSets { get; set; } = new();
    public int LocalMixedPort { get; set; } = 1080;
    public bool ProxyWasEnabledByUs { get; set; }
    public ProxySettingsSnapshotModel? PreviousProxySettings { get; set; }
    public string ProxyOverride { get; set; } = "localhost;127.*;10.*;192.168.*;172.16.*";
    public string TunInterfaceName { get; set; } = "NothingVpn";
    public string TunAddressCidr { get; set; } = "auto";
    public int TunMtu { get; set; } = 1500;
    public string TunStack { get; set; } = "";
    public bool TunAutoRoute { get; set; } = true;
    public bool TunStrictRoute { get; set; } = true;
    public string DnsMode { get; set; } = "doh";
    public string DohServer { get; set; } = "8.8.8.8";
    public string DohPath { get; set; } = "/dns-query";
    public string DohSni { get; set; } = "dns.google";
    public string DnsDetour { get; set; } = "direct";
    public bool DebugLogs { get; set; }
    public string SingBoxLogLevel { get; set; } = "warn";
    public string? TrustedSingBoxSha256 { get; set; }
    public string? UpdateDismissedModalForTag { get; set; }
    public string? LastRecordedAppSemver { get; set; }
    public DateTimeOffset? UpdateLastCheckUtc { get; set; }
}


using NothingVpn.Domain.Policies;

namespace NothingVpn.Domain.Models;

public sealed class DnsSettings
{
    public DnsMode Mode { get; set; } = DnsPolicy.DefaultMode;
    public string DohServer { get; set; } = DnsPolicy.DefaultDohServer;
    public string DohPath { get; set; } = DnsPolicy.DefaultDohPath;
    public string DohSni { get; set; } = DnsPolicy.DefaultDohSni;
    public DnsDetour Detour { get; set; } = DnsPolicy.DefaultDetour;
}

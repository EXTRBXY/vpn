using NothingVpn.Domain.Policies;

namespace NothingVpn.Domain.Models;

public sealed class DnsSettings
{
    public string Mode { get; set; } = DnsPolicy.DefaultMode;
    public string DohServer { get; set; } = DnsPolicy.DefaultDohServer;
    public string DohPath { get; set; } = DnsPolicy.DefaultDohPath;
    public string DohSni { get; set; } = DnsPolicy.DefaultDohSni;
    public string Detour { get; set; } = DnsPolicy.DefaultDetour;
}


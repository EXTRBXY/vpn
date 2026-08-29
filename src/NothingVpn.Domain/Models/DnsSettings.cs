namespace NothingVpn.Domain.Models;

public sealed class DnsSettings
{
    public string Mode { get; set; } = "doh";
    public string DohServer { get; set; } = "8.8.8.8";
    public string DohPath { get; set; } = "/dns-query";
    public string DohSni { get; set; } = "dns.google";
    public string Detour { get; set; } = "direct";
}


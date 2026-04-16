namespace NothingVpn.Application.Models;

public sealed class ProxySettingsSnapshotModel
{
    public bool ProxyEnable { get; set; }
    public string? ProxyServer { get; set; }
    public string? ProxyOverride { get; set; }
}


namespace NothingVpn.Application.Models;

public sealed class VpnConnectionStatus
{
    public bool IsRunning { get; init; }
    public string Mode { get; init; } = "proxy";
    public string? ActiveProfileId { get; init; }
}


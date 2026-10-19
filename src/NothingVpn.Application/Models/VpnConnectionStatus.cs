using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Models;

public sealed class VpnConnectionStatus
{
    public bool IsRunning { get; init; }
    public ConnectionMode Mode { get; init; } = ConnectionPolicy.DefaultMode;
    public string? ActiveProfileId { get; init; }
}


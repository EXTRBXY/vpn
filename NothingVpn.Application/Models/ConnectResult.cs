namespace NothingVpn.Application.Models;

public sealed class ConnectResult
{
    public bool Started { get; init; }
    public bool RequiresElevation { get; init; }
    public string? ElevationArgs { get; init; }
}


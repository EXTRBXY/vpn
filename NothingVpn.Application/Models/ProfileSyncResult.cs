namespace NothingVpn.Application.Models;

public sealed class ProfileSyncResult
{
    public IReadOnlyList<VpnProfile> Profiles { get; init; } = Array.Empty<VpnProfile>();
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Removed { get; init; }
}

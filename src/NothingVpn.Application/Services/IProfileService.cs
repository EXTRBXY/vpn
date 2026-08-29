using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface IProfileService
{
    IReadOnlyList<VpnProfile> GetProfiles();
    IReadOnlyList<VpnProfile> ImportFromVlessLink(string link);
    IReadOnlyList<VpnProfile> DeleteProfile(string profileId);

    bool TryParseVlessLink(string link, out VpnProfile profile);
    VpnProfile UpsertFromVlessLink(string link, string? nameOverride);
}


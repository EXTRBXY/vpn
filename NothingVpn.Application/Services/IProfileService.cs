using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface IProfileService
{
    IReadOnlyList<VpnProfile> GetProfiles();
    IReadOnlyList<VpnProfile> ImportFromVlessLink(string link);
}


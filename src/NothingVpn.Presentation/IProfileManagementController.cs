using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Presentation;

public interface IProfileManagementController
{
    ProfileManagementSnapshot Load();
    bool TryParse(string link, out VpnProfile profile);
    VpnProfile Add(string link, string? nameOverride, OutboundEngine engine);
    VpnProfile Edit(string existingProfileId, string link, string? nameOverride, OutboundEngine engine);
    ProfileManagementSnapshot Delete(string profileId);
}

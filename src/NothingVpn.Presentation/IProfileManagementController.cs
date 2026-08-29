using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public interface IProfileManagementController
{
    ProfileManagementSnapshot Load();
    bool TryParse(string link, out VpnProfile profile);
    VpnProfile Add(string link, string? nameOverride);
    VpnProfile Edit(string existingProfileId, string link, string? nameOverride);
    ProfileManagementSnapshot Delete(string profileId);
}

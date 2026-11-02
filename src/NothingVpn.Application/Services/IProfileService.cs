using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Services;

public interface IProfileService
{
    IReadOnlyList<VpnProfile> GetProfiles();
    IReadOnlyList<VpnProfile> ImportFromVlessLink(string link);
    IReadOnlyList<VpnProfile> DeleteProfile(string profileId);

    bool TryParseVlessLink(string link, out VpnProfile profile);

    /// <param name="engine">Выбор движка пользователем; несовместимый с транспортом отклоняется.</param>
    VpnProfile UpsertFromVlessLink(string link, string? nameOverride, OutboundEngine engine);
}

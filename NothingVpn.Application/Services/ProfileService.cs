using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;

namespace NothingVpn.Application.Services;

public sealed class ProfileService(IProfileStorePort profileStore, IProfileParserPort profileParser) : IProfileService
{
    public IReadOnlyList<VpnProfile> GetProfiles() => profileStore.Load();

    public IReadOnlyList<VpnProfile> ImportFromVlessLink(string link)
    {
        var parsed = profileParser.ParseVlessLink(link);
        return profileStore.Upsert(parsed);
    }
}


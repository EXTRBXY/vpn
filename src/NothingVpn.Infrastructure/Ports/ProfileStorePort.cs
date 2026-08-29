using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Mappers;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Infrastructure.Ports;

public sealed class ProfileStorePort : IProfileStorePort
{
    private readonly JsonProfileStore _store;

    public ProfileStorePort(IAppPathsPort appPathsPort)
    {
        _store = new JsonProfileStore(appPathsPort.Get().ProfilesJsonPath);
    }

    public IReadOnlyList<VpnProfile> Load() => _store.Load().Select(LegacyModelMapper.ToModel).ToList();

    public IReadOnlyList<VpnProfile> Upsert(VpnProfile profile)
        => _store.Upsert(LegacyModelMapper.ToLegacy(profile)).Select(LegacyModelMapper.ToModel).ToList();

    public IReadOnlyList<VpnProfile> Delete(string profileId)
        => _store.Delete(profileId).Select(LegacyModelMapper.ToModel).ToList();

    public ProfileSyncResult SyncForSubscription(string subscriptionId, IReadOnlyList<VpnProfile> profilesFromSubscription)
    {
        var legacyProfiles = profilesFromSubscription.Select(LegacyModelMapper.ToLegacy).ToList();
        var (profiles, added, updated, removed) = _store.SyncForSubscription(subscriptionId, legacyProfiles);
        return new ProfileSyncResult
        {
            Profiles = profiles.Select(LegacyModelMapper.ToModel).ToList(),
            Added = added,
            Updated = updated,
            Removed = removed
        };
    }

    public IReadOnlyList<VpnProfile> DeleteBySubscription(string subscriptionId)
        => _store.DeleteBySubscription(subscriptionId).Select(LegacyModelMapper.ToModel).ToList();
}


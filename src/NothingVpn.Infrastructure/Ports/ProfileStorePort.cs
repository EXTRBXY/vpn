using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Infrastructure.Ports;

public sealed class ProfileStorePort : IProfileStorePort
{
    private readonly JsonProfileStore _store;

    public ProfileStorePort(IAppPathsPort appPathsPort)
    {
        _store = new JsonProfileStore(appPathsPort.Get().ProfilesJsonPath);
    }

    public IReadOnlyList<VpnProfile> Load() => _store.Load();

    public IReadOnlyList<VpnProfile> Upsert(VpnProfile profile)
        => _store.Upsert(profile);

    public IReadOnlyList<VpnProfile> Delete(string profileId)
        => _store.Delete(profileId);

    public ProfileSyncResult SyncForSubscription(string subscriptionId, IReadOnlyList<VpnProfile> profilesFromSubscription)
    {
        var (profiles, added, updated, removed) = _store.SyncForSubscription(subscriptionId, profilesFromSubscription);
        return new ProfileSyncResult
        {
            Profiles = profiles,
            Added = added,
            Updated = updated,
            Removed = removed
        };
    }

    public IReadOnlyList<VpnProfile> DeleteBySubscription(string subscriptionId)
        => _store.DeleteBySubscription(subscriptionId);
}


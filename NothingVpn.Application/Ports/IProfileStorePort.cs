using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface IProfileStorePort
{
    IReadOnlyList<VpnProfile> Load();
    IReadOnlyList<VpnProfile> Upsert(VpnProfile profile);
    IReadOnlyList<VpnProfile> Delete(string profileId);
    ProfileSyncResult SyncForSubscription(string subscriptionId, IReadOnlyList<VpnProfile> profilesFromSubscription);
    IReadOnlyList<VpnProfile> DeleteBySubscription(string subscriptionId);
}


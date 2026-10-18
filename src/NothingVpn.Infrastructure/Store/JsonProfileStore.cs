using System.Text.Json;
using NothingVpn.Application.Models;
using NothingVpn.Infrastructure.Security;

namespace NothingVpn.Infrastructure.Store;

internal sealed class JsonProfileStore
{
    private readonly string _path;

    public JsonProfileStore(string path) => _path = path;

    public IReadOnlyList<VpnProfile> Load()
    {
        return DpapiJsonFile.LoadOrDefault(_path, defaultFactory: () => new List<VpnProfile>());
    }

    public IReadOnlyList<VpnProfile> Upsert(VpnProfile profile)
    {
        var list = Load().ToList();
        var idx = list.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) list[idx] = profile;
        else list.Add(profile);

        Save(list);
        return list;
    }

    public IReadOnlyList<VpnProfile> Delete(string profileId)
    {
        var list = Load().ToList();
        list.RemoveAll(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
        Save(list);
        return list;
    }

    public (IReadOnlyList<VpnProfile> Profiles, int Added, int Updated, int Removed) SyncForSubscription(
        string subscriptionId,
        IReadOnlyList<VpnProfile> profilesFromSubscription)
    {
        var list = Load().ToList();
        var existingForSub = list
            .Where(p => string.Equals(p.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var existingIds = existingForSub.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newIds = profilesFromSubscription.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = list.RemoveAll(p =>
            string.Equals(p.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase) &&
            !newIds.Contains(p.Id));

        var added = 0;
        var updated = 0;
        foreach (var profile in profilesFromSubscription)
        {
            profile.SubscriptionId = subscriptionId;
            var wasExisting = existingIds.Contains(profile.Id);
            var idx = list.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                list[idx] = profile;
                if (wasExisting)
                    updated++;
                else
                    added++;
            }
            else
            {
                list.Add(profile);
                added++;
            }
        }

        Save(list);
        return (list, added, updated, removed);
    }

    public IReadOnlyList<VpnProfile> DeleteBySubscription(string subscriptionId)
    {
        var list = Load().ToList();
        list.RemoveAll(p => string.Equals(p.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
        Save(list);
        return list;
    }

    private void Save(List<VpnProfile> profiles)
    {
        DpapiJsonFile.Save(_path, profiles);
    }
}


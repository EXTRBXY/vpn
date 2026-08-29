using System.Text.Json;
using NothingVpn.Infrastructure.Security;

namespace NothingVpn.Infrastructure.Store;

internal sealed class SubscriptionRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastSyncUtc { get; set; }
    public string? LastError { get; set; }
    public int UpdateIntervalHours { get; set; } = 24;
    public long Upload { get; set; }
    public long Download { get; set; }
    public long Total { get; set; }
    public DateTimeOffset? ExpireUtc { get; set; }
}

internal sealed class JsonSubscriptionStore
{
    private readonly string _path;

    public JsonSubscriptionStore(string path) => _path = path;

    public IReadOnlyList<SubscriptionRecord> Load()
        => DpapiJsonFile.LoadOrDefault(_path, defaultFactory: () => new List<SubscriptionRecord>());

    public SubscriptionRecord Upsert(SubscriptionRecord subscription)
    {
        var list = Load().ToList();
        var idx = list.FindIndex(s => string.Equals(s.Id, subscription.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            list[idx] = subscription;
        else
            list.Add(subscription);

        Save(list);
        return subscription;
    }

    public IReadOnlyList<SubscriptionRecord> Delete(string subscriptionId)
    {
        var list = Load().ToList();
        list.RemoveAll(s => string.Equals(s.Id, subscriptionId, StringComparison.OrdinalIgnoreCase));
        Save(list);
        return list;
    }

    private void Save(List<SubscriptionRecord> subscriptions)
        => DpapiJsonFile.Save(_path, subscriptions);
}

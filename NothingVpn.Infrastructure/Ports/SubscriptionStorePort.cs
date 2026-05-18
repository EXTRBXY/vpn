using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Tray.Internal.Store;

namespace NothingVpn.Infrastructure.Ports;

public sealed class SubscriptionStorePort : ISubscriptionStorePort
{
    private readonly JsonSubscriptionStore _store;

    public SubscriptionStorePort(IAppPathsPort appPathsPort)
        => _store = new JsonSubscriptionStore(appPathsPort.Get().SubscriptionsJsonPath);

    public IReadOnlyList<SubscriptionModel> Load()
        => _store.Load().Select(ToModel).ToList();

    public SubscriptionModel Upsert(SubscriptionModel subscription)
        => ToModel(_store.Upsert(ToRecord(subscription)));

    public IReadOnlyList<SubscriptionModel> Delete(string subscriptionId)
        => _store.Delete(subscriptionId).Select(ToModel).ToList();

    private static SubscriptionModel ToModel(SubscriptionRecord source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Url = source.Url,
        Enabled = source.Enabled,
        LastSyncUtc = source.LastSyncUtc,
        LastError = source.LastError,
        UpdateIntervalHours = source.UpdateIntervalHours,
        UserInfo = new SubscriptionUserInfoModel
        {
            Upload = source.Upload,
            Download = source.Download,
            Total = source.Total,
            ExpireUtc = source.ExpireUtc
        }
    };

    private static SubscriptionRecord ToRecord(SubscriptionModel source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Url = source.Url,
        Enabled = source.Enabled,
        LastSyncUtc = source.LastSyncUtc,
        LastError = source.LastError,
        UpdateIntervalHours = source.UpdateIntervalHours,
        Upload = source.UserInfo.Upload,
        Download = source.UserInfo.Download,
        Total = source.UserInfo.Total,
        ExpireUtc = source.UserInfo.ExpireUtc
    };
}

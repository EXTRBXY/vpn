using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface ISubscriptionStorePort
{
    IReadOnlyList<SubscriptionModel> Load();
    SubscriptionModel Upsert(SubscriptionModel subscription);
    IReadOnlyList<SubscriptionModel> Delete(string subscriptionId);
}

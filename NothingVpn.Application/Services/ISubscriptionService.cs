using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface ISubscriptionService
{
    IReadOnlyList<SubscriptionModel> GetSubscriptions();
    SubscriptionModel? GetSubscription(string subscriptionId);
    SubscriptionModel AddOrUpdate(string? subscriptionId, string name, string url, bool enabled = true);
    void Delete(string subscriptionId);
    Task<SubscriptionRefreshResult> RefreshAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionRefreshResult>> RefreshAllDueAsync(CancellationToken cancellationToken = default);
    bool IsDue(SubscriptionModel subscription, DateTimeOffset utcNow);
}

using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public interface ISubscriptionManagementController
{
    IReadOnlyList<SubscriptionModel> Load();
    SubscriptionModel Save(string? id, string name, string url, bool enabled);
    void Delete(string id);
    Task<SubscriptionRefreshResult> RefreshAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionRefreshResult>> RefreshAllAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default);
}

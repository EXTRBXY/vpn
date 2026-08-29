using NothingVpn.Application.Models;
using NothingVpn.Application.Services;

namespace NothingVpn.Presentation;

public sealed class SubscriptionManagementController : ISubscriptionManagementController
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionManagementController(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<SubscriptionModel> Load() => _subscriptionService.GetSubscriptions();

    public SubscriptionModel Save(string? id, string name, string url, bool enabled) =>
        _subscriptionService.AddOrUpdate(id, name, url, enabled);

    public void Delete(string id) => _subscriptionService.Delete(id);

    public Task<SubscriptionRefreshResult> RefreshAsync(string id, CancellationToken cancellationToken = default) =>
        _subscriptionService.RefreshAsync(id, cancellationToken);

    public async Task<IReadOnlyList<SubscriptionRefreshResult>> RefreshAllAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SubscriptionRefreshResult>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await _subscriptionService.RefreshAsync(id, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new SubscriptionRefreshResult
                {
                    SubscriptionId = id,
                    Success = false,
                    Error = ex.Message
                });
            }
        }
        return results;
    }
}

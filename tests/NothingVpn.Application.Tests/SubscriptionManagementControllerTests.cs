using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class SubscriptionManagementControllerTests
{
    [Fact]
    public async Task RefreshAllAsync_RefreshesEveryRequestedSubscriptionInOrder()
    {
        var service = new FakeSubscriptionService();
        var controller = new SubscriptionManagementController(service);

        var results = await controller.RefreshAllAsync(new[] { "s2", "s1" });

        Assert.Equal(new[] { "s2", "s1" }, service.RefreshedIds);
        Assert.Equal(new[] { "s2", "s1" }, results.Select(r => r.SubscriptionId));
    }

    [Fact]
    public async Task RefreshAllAsync_RecordsFailureAndContinuesWithNextSubscription()
    {
        var service = new FakeSubscriptionService { ThrowForId = "broken" };
        var controller = new SubscriptionManagementController(service);

        var results = await controller.RefreshAllAsync(new[] { "broken", "working" });

        Assert.False(results[0].Success);
        Assert.Equal("Refresh failed.", results[0].Error);
        Assert.True(results[1].Success);
        Assert.Equal(new[] { "broken", "working" }, service.RefreshedIds);
    }

    [Fact]
    public void SaveAndDelete_DelegateManagementOperations()
    {
        var service = new FakeSubscriptionService();
        var controller = new SubscriptionManagementController(service);

        var saved = controller.Save(null, "Main", "https://example.test/sub", true);
        controller.Delete(saved.Id);

        Assert.Equal("Main", service.LastSaved?.Name);
        Assert.Equal(saved.Id, service.DeletedId);
    }

    private sealed class FakeSubscriptionService : ISubscriptionService
    {
        public List<string> RefreshedIds { get; } = new();
        public SubscriptionModel? LastSaved { get; private set; }
        public string? DeletedId { get; private set; }
        public string? ThrowForId { get; init; }

        public IReadOnlyList<SubscriptionModel> GetSubscriptions() => Array.Empty<SubscriptionModel>();
        public SubscriptionModel? GetSubscription(string subscriptionId) => null;

        public SubscriptionModel AddOrUpdate(string? subscriptionId, string name, string url, bool enabled = true)
        {
            LastSaved = new SubscriptionModel { Id = subscriptionId ?? "new", Name = name, Url = url, Enabled = enabled };
            return LastSaved;
        }

        public void Delete(string subscriptionId) => DeletedId = subscriptionId;

        public Task<SubscriptionRefreshResult> RefreshAsync(string subscriptionId, CancellationToken cancellationToken = default)
        {
            RefreshedIds.Add(subscriptionId);
            if (subscriptionId == ThrowForId)
                throw new InvalidOperationException("Refresh failed.");
            return Task.FromResult(new SubscriptionRefreshResult { SubscriptionId = subscriptionId, Success = true });
        }

        public Task<IReadOnlyList<SubscriptionRefreshResult>> RefreshAllDueAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool IsDue(SubscriptionModel subscription, DateTimeOffset utcNow) => throw new NotSupportedException();
    }
}

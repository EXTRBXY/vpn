using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;

namespace NothingVpn.Application.Tests;

public sealed class SubscriptionServiceSyncTests
{
    [Fact]
    public async Task Refresh_ReplacesStaleProfiles_AndKeepsManual()
    {
        var profileStore = new InMemoryProfileStorePort();
        var subscriptionStore = new InMemorySubscriptionStorePort();
        var fetcher = new FakeSubscriptionFetcher(
            """
            vless://11111111-1111-1111-1111-111111111111@host1.example:443?encryption=none&security=tls&type=tcp#node1
            """);

        var manual = new VpnProfile
        {
            Id = "manual1",
            Name = "Manual",
            Uuid = "22222222-2222-2222-2222-222222222222",
            Host = "manual.example",
            Port = 443,
            Type = "tcp",
            Security = "tls",
            Encryption = "none"
        };
        profileStore.Upsert(manual);

        var subscription = subscriptionStore.Upsert(new SubscriptionModel
        {
            Id = "sub1",
            Name = "Test Sub",
            Url = "https://example.com/sub/token",
            Enabled = true
        });

        var stale = new VpnProfile
        {
            Id = "stale1",
            SubscriptionId = subscription.Id,
            Name = "Stale",
            Uuid = "33333333-3333-3333-3333-333333333333",
            Host = "stale.example",
            Port = 443,
            Type = "tcp",
            Security = "tls",
            Encryption = "none"
        };
        profileStore.Upsert(stale);

        var settings = new InMemorySettingsService();
        settings.GetState().ActiveProfileId = stale.Id;

        var parser = new FakeProfileParserPort();
        var service = new SubscriptionService(
            subscriptionStore,
            fetcher,
            profileStore,
            parser,
            settings);

        var result = await service.RefreshAsync(subscription.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.Added + result.Updated);
        Assert.True(result.Removed >= 0);

        var profiles = profileStore.Load();
        Assert.Contains(profiles, p => p.Id == manual.Id && p.SubscriptionId is null);
        Assert.DoesNotContain(profiles, p => p.Id == stale.Id);
        Assert.Contains(profiles, p => p.SubscriptionId == subscription.Id && p.Host == "host1.example");
        Assert.Null(settings.GetState().ActiveProfileId);
        Assert.True(result.ActiveProfileCleared);
    }

    private sealed class InMemoryProfileStorePort : IProfileStorePort
    {
        private readonly List<VpnProfile> _profiles = new();

        public IReadOnlyList<VpnProfile> Load() => _profiles.ToList();

        public IReadOnlyList<VpnProfile> Upsert(VpnProfile profile)
        {
            _profiles.RemoveAll(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            _profiles.Add(profile);
            return Load();
        }

        public IReadOnlyList<VpnProfile> Delete(string profileId)
        {
            _profiles.RemoveAll(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
            return Load();
        }

        public ProfileSyncResult SyncForSubscription(string subscriptionId, IReadOnlyList<VpnProfile> profilesFromSubscription)
        {
            var existing = _profiles
                .Where(p => string.Equals(p.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newIds = profilesFromSubscription.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = _profiles.RemoveAll(p =>
                string.Equals(p.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase) &&
                !newIds.Contains(p.Id));

            var added = 0;
            var updated = 0;
            foreach (var profile in profilesFromSubscription)
            {
                profile.SubscriptionId = subscriptionId;
                var wasExisting = existing.Contains(profile.Id);
                var idx = _profiles.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    _profiles[idx] = profile;
                    if (wasExisting) updated++; else added++;
                }
                else
                {
                    _profiles.Add(profile);
                    added++;
                }
            }

            return new ProfileSyncResult
            {
                Profiles = Load(),
                Added = added,
                Updated = updated,
                Removed = removed
            };
        }

        public IReadOnlyList<VpnProfile> DeleteBySubscription(string subscriptionId)
        {
            _profiles.RemoveAll(p => string.Equals(p.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
            return Load();
        }
    }

    private sealed class InMemorySubscriptionStorePort : ISubscriptionStorePort
    {
        private readonly List<SubscriptionModel> _items = new();

        public IReadOnlyList<SubscriptionModel> Load() => _items.ToList();

        public SubscriptionModel Upsert(SubscriptionModel subscription)
        {
            _items.RemoveAll(s => string.Equals(s.Id, subscription.Id, StringComparison.OrdinalIgnoreCase));
            _items.Add(subscription);
            return subscription;
        }

        public IReadOnlyList<SubscriptionModel> Delete(string subscriptionId)
        {
            _items.RemoveAll(s => string.Equals(s.Id, subscriptionId, StringComparison.OrdinalIgnoreCase));
            return Load();
        }
    }

    private sealed class FakeSubscriptionFetcher(string body) : ISubscriptionFetcherPort
    {
        public Task<SubscriptionFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Subscription-Userinfo"] = "upload=10; download=20; total=100; expire=0",
                ["Profile-Update-Interval"] = "6"
            };
            return Task.FromResult(new SubscriptionFetchResult
            {
                Success = true,
                StatusCode = 200,
                Body = body,
                Headers = headers
            });
        }
    }

    private sealed class FakeProfileParserPort : IProfileParserPort
    {
        public VpnProfile ParseVlessLink(string link)
        {
            var uri = new Uri(link.Trim());
            return new VpnProfile
            {
                Id = "id-" + uri.Host,
                Name = string.IsNullOrWhiteSpace(uri.Fragment) ? uri.Host : uri.Fragment.TrimStart('#'),
                Uuid = uri.UserInfo,
                Host = uri.Host,
                Port = uri.Port,
                Type = "tcp",
                Security = "tls",
                Encryption = "none"
            };
        }
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        public event EventHandler<AppStateModel>? StateChanged;
        private AppStateModel _state = new();
        public AppStateModel GetState() => _state;
        public void SaveState(AppStateModel state)
        {
            _state = state;
            StateChanged?.Invoke(this, state);
        }
        public void UpdateMode(string mode) => _state.Mode = mode;
        public void UpdateDns(string mode, string dohServer, string dohPath, string dohSni, string detour) { }
        public void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets) { }
        public void UpdateTunApps(IReadOnlyCollection<string> paths) { }
    }
}

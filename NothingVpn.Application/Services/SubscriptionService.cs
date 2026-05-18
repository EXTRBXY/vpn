using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Subscriptions;

namespace NothingVpn.Application.Services;

public sealed class SubscriptionService(
    ISubscriptionStorePort subscriptionStore,
    ISubscriptionFetcherPort fetcher,
    IProfileStorePort profileStore,
    IProfileParserPort profileParser,
    ISettingsService settingsService) : ISubscriptionService
{
    public IReadOnlyList<SubscriptionModel> GetSubscriptions() => subscriptionStore.Load();

    public SubscriptionModel? GetSubscription(string subscriptionId)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            return null;
        return subscriptionStore.Load()
            .FirstOrDefault(s => string.Equals(s.Id, subscriptionId, StringComparison.OrdinalIgnoreCase));
    }

    public SubscriptionModel AddOrUpdate(string? subscriptionId, string name, string url, bool enabled = true)
    {
        SubscriptionUrlValidator.EnsureValid(url);

        var trimmedName = SanitizeName(name);
        if (trimmedName.Length == 0)
            throw new ArgumentException("Название подписки обязательно.", nameof(name));

        var id = string.IsNullOrWhiteSpace(subscriptionId)
            ? Guid.NewGuid().ToString("N")
            : subscriptionId.Trim();

        var existing = GetSubscription(id);
        var model = new SubscriptionModel
        {
            Id = id,
            Name = trimmedName,
            Url = url.Trim(),
            Enabled = enabled,
            LastSyncUtc = existing?.LastSyncUtc,
            LastError = existing?.LastError,
            UpdateIntervalHours = existing?.UpdateIntervalHours ?? SubscriptionSyncPolicy.DefaultUpdateIntervalHours,
            UserInfo = existing?.UserInfo ?? new SubscriptionUserInfoModel()
        };

        return subscriptionStore.Upsert(model);
    }

    public void Delete(string subscriptionId)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new ArgumentException("Subscription id is required.", nameof(subscriptionId));

        profileStore.DeleteBySubscription(subscriptionId);
        subscriptionStore.Delete(subscriptionId);
        ClearActiveProfileIfMissing();
    }

    public async Task<SubscriptionRefreshResult> RefreshAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = GetSubscription(subscriptionId)
            ?? throw new InvalidOperationException("Подписка не найдена.");

        try
        {
            var fetch = await fetcher.FetchAsync(subscription.Url, cancellationToken).ConfigureAwait(false);
            if (!fetch.Success)
            {
                var error = fetch.Error ?? "Не удалось загрузить подписку.";
                SaveSubscriptionError(subscription, error);
                return Failed(subscriptionId, error);
            }

            var decoded = SubscriptionBodyDecoder.DecodeBody(fetch.Body);
            var extraction = SubscriptionLinkExtractor.Extract(decoded);
            var parsedProfiles = new List<VpnProfile>();
            var parseErrors = new List<string>();

            foreach (var link in extraction.VlessLinks)
            {
                try
                {
                    var profile = profileParser.ParseVlessLink(link);
                    profile.SubscriptionId = subscription.Id;
                    parsedProfiles.Add(profile);
                }
                catch (Exception ex)
                {
                    parseErrors.Add(ex.Message);
                }
            }

            if (parsedProfiles.Count == 0)
            {
                var error = parseErrors.Count > 0
                    ? "Не найдено VLESS-узлов: " + parseErrors[0]
                    : "В подписке нет поддерживаемых VLESS-ссылок.";
                SaveSubscriptionError(subscription, error);
                return new SubscriptionRefreshResult
                {
                    SubscriptionId = subscriptionId,
                    Success = false,
                    Error = error,
                    SkippedNonVless = extraction.SkippedNonVlessLines,
                    ParseErrors = parseErrors
                };
            }

            var headers = SubscriptionHeadersParser.Parse(fetch.Headers);
            ApplyHeaders(subscription, headers);

            var sync = profileStore.SyncForSubscription(subscription.Id, parsedProfiles);
            subscription.LastSyncUtc = DateTimeOffset.UtcNow;
            subscription.LastError = null;
            subscriptionStore.Upsert(subscription);

            var activeCleared = ClearActiveProfileIfMissing();

            return new SubscriptionRefreshResult
            {
                SubscriptionId = subscriptionId,
                Success = true,
                Added = sync.Added,
                Updated = sync.Updated,
                Removed = sync.Removed,
                SkippedNonVless = extraction.SkippedNonVlessLines,
                ParseErrors = parseErrors,
                ActiveProfileCleared = activeCleared
            };
        }
        catch (Exception ex)
        {
            SaveSubscriptionError(subscription, ex.Message);
            return Failed(subscriptionId, ex.Message);
        }
    }

    public async Task<IReadOnlyList<SubscriptionRefreshResult>> RefreshAllDueAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var results = new List<SubscriptionRefreshResult>();
        foreach (var subscription in subscriptionStore.Load())
        {
            if (!subscription.Enabled || !IsDue(subscription, now))
                continue;

            results.Add(await RefreshAsync(subscription.Id, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public bool IsDue(SubscriptionModel subscription, DateTimeOffset utcNow)
    {
        if (!subscription.Enabled)
            return false;

        if (subscription.LastSyncUtc is null)
            return true;

        var interval = subscription.UpdateIntervalHours;
        if (interval < SubscriptionSyncPolicy.MinUpdateIntervalHours)
            interval = SubscriptionSyncPolicy.DefaultUpdateIntervalHours;

        return subscription.LastSyncUtc.Value.AddHours(interval) <= utcNow;
    }

    private void ApplyHeaders(SubscriptionModel subscription, SubscriptionHeaders headers)
    {
        if (!string.IsNullOrWhiteSpace(headers.ProfileTitle) &&
            (string.IsNullOrWhiteSpace(subscription.Name) || subscription.Name == "Подписка"))
            subscription.Name = SanitizeName(headers.ProfileTitle);

        if (headers.UpdateIntervalHours is int hours)
            subscription.UpdateIntervalHours = hours;

        if (headers.UserInfo is SubscriptionUserInfo userInfo)
        {
            subscription.UserInfo = new SubscriptionUserInfoModel
            {
                Upload = userInfo.Upload,
                Download = userInfo.Download,
                Total = userInfo.Total,
                ExpireUtc = userInfo.ExpireUtc
            };
        }
    }

    private void SaveSubscriptionError(SubscriptionModel subscription, string error)
    {
        subscription.LastError = TruncateError(error);
        subscriptionStore.Upsert(subscription);
    }

    private static SubscriptionRefreshResult Failed(string subscriptionId, string error) => new()
    {
        SubscriptionId = subscriptionId,
        Success = false,
        Error = error
    };

    private bool ClearActiveProfileIfMissing()
    {
        var state = settingsService.GetState();
        var activeId = state.ActiveProfileId;
        if (string.IsNullOrWhiteSpace(activeId))
            return false;

        var exists = profileStore.Load().Any(p => string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase));
        if (exists)
            return false;

        state.ActiveProfileId = null;
        settingsService.SaveState(state);
        return true;
    }

    private static string SanitizeName(string name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return string.Empty;
        return n.Length > 64 ? n[..64] : n;
    }

    private static string TruncateError(string error)
    {
        var e = (error ?? "").Trim();
        if (e.Length <= 200) return e;
        return e[..200];
    }
}

using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Subscriptions;

public sealed class SubscriptionHeaders
{
    public SubscriptionUserInfo? UserInfo { get; init; }
    public int? UpdateIntervalHours { get; init; }
    public string? ProfileTitle { get; init; }
}

public static class SubscriptionHeadersParser
{
    public static SubscriptionHeaders Parse(IReadOnlyDictionary<string, string> headers)
    {
        SubscriptionUserInfo? userInfo = null;
        if (TryGetHeader(headers, "Subscription-Userinfo", out var userinfoHeader) &&
            SubscriptionUserinfoParser.TryParse(userinfoHeader, out var parsed))
            userInfo = parsed;

        int? intervalHours = null;
        if (TryGetHeader(headers, "Profile-Update-Interval", out var intervalHeader) &&
            int.TryParse(intervalHeader.Trim(), out var hours) &&
            hours > 0)
            intervalHours = ClampInterval(hours);

        string? title = null;
        if (TryGetHeader(headers, "Profile-Title", out var titleHeader))
            title = DecodeProfileTitle(titleHeader);

        return new SubscriptionHeaders
        {
            UserInfo = userInfo,
            UpdateIntervalHours = intervalHours,
            ProfileTitle = title
        };
    }

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string value)
    {
        if (headers.TryGetValue(name, out value!) && !string.IsNullOrWhiteSpace(value))
            return true;

        foreach (var kv in headers)
        {
            if (!kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(kv.Value))
            {
                value = kv.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string? DecodeProfileTitle(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return null;

        const string prefix = "base64:";
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var bytes = Convert.FromBase64String(trimmed[prefix.Length..]);
                var decoded = System.Text.Encoding.UTF8.GetString(bytes).Trim();
                return decoded.Length == 0 ? null : decoded;
            }
            catch
            {
                return null;
            }
        }

        return trimmed;
    }

    private static int ClampInterval(int hours)
    {
        if (hours < SubscriptionSyncPolicy.MinUpdateIntervalHours)
            return SubscriptionSyncPolicy.MinUpdateIntervalHours;
        if (hours > SubscriptionSyncPolicy.MaxUpdateIntervalHours)
            return SubscriptionSyncPolicy.MaxUpdateIntervalHours;
        return hours;
    }
}

using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Subscriptions;

public static class SubscriptionUserinfoParser
{
    public static bool TryParse(string? header, out SubscriptionUserInfo info)
    {
        info = new SubscriptionUserInfo();
        if (string.IsNullOrWhiteSpace(header))
            return false;

        long upload = 0;
        long download = 0;
        long total = 0;
        long expire = 0;
        var found = false;

        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
                continue;

            var key = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            if (!long.TryParse(value, out var number))
                continue;

            found = true;
            if (key.Equals("upload", StringComparison.OrdinalIgnoreCase))
                upload = number;
            else if (key.Equals("download", StringComparison.OrdinalIgnoreCase))
                download = number;
            else if (key.Equals("total", StringComparison.OrdinalIgnoreCase))
                total = number;
            else if (key.Equals("expire", StringComparison.OrdinalIgnoreCase))
                expire = number;
        }

        if (!found)
            return false;

        DateTimeOffset? expireUtc = null;
        if (expire > 0)
            expireUtc = DateTimeOffset.FromUnixTimeSeconds(expire);

        info = new SubscriptionUserInfo
        {
            Upload = upload,
            Download = download,
            Total = total,
            ExpireUtc = expireUtc
        };
        return true;
    }
}

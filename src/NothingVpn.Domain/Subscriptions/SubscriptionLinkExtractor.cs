namespace NothingVpn.Domain.Subscriptions;

public sealed class SubscriptionLinkExtractionResult
{
    public IReadOnlyList<string> VlessLinks { get; init; } = Array.Empty<string>();
    public int SkippedNonVlessLines { get; init; }
}

public static class SubscriptionLinkExtractor
{
    public static SubscriptionLinkExtractionResult Extract(string decodedBody)
    {
        if (string.IsNullOrWhiteSpace(decodedBody))
            return new SubscriptionLinkExtractionResult();

        var vless = new List<string>();
        var skipped = 0;

        foreach (var line in decodedBody.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            {
                vless.Add(trimmed);
                continue;
            }

            if (IsOtherProxyScheme(trimmed))
                skipped++;
        }

        return new SubscriptionLinkExtractionResult
        {
            VlessLinks = vless,
            SkippedNonVlessLines = skipped
        };
    }

    private static bool IsOtherProxyScheme(string line)
        => line.Contains("://", StringComparison.Ordinal);
}

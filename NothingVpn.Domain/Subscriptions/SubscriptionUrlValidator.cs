namespace NothingVpn.Domain.Subscriptions;

public static class SubscriptionUrlValidator
{
    public static void EnsureValid(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL подписки обязателен.", nameof(url));

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("Некорректный URL подписки.", nameof(url));

        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("URL подписки должен использовать http или https.", nameof(url));
    }
}

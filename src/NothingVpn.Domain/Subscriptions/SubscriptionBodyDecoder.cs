using System.Text;

namespace NothingVpn.Domain.Subscriptions;

public static class SubscriptionBodyDecoder
{
    public static string DecodeBody(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Пустой ответ подписки.");

        var trimmed = raw.Trim();
        if (ContainsVlessLink(trimmed))
            return trimmed;

        if (!TryDecodeBase64Utf8(trimmed, out var decoded) || string.IsNullOrWhiteSpace(decoded))
            throw new InvalidOperationException("Не удалось декодировать тело подписки.");

        return decoded.Trim();
    }

    private static bool ContainsVlessLink(string text)
        => text.Contains("vless://", StringComparison.OrdinalIgnoreCase);

    private static bool TryDecodeBase64Utf8(string input, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            var normalized = input.Replace("\r", "").Replace("\n", "").Trim();
            var bytes = Convert.FromBase64String(normalized);
            decoded = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class ProxyConnectionPolicy
{
    public const string DefaultProxyOverride = "localhost;127.*;10.*;192.168.*;172.16.*";
    public const int MaxProxyOverrideLength = 2048;

    public static void Normalize(ProxyConnectionSettings settings)
    {
        settings.ProxyOverride = NormalizeProxyOverride(settings.ProxyOverride);
    }

    public static void Validate(ProxyConnectionSettings settings)
    {
        if (ContainsInvalidChars(settings.ProxyOverride ?? string.Empty))
            throw new ArgumentException("ProxyOverride содержит недопустимые символы.");

        Normalize(settings);
    }

    public static string NormalizeProxyOverride(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0 || ContainsInvalidChars(trimmed))
            return DefaultProxyOverride;

        return trimmed.Length > MaxProxyOverrideLength
            ? trimmed[..MaxProxyOverrideLength]
            : trimmed;
    }

    private static bool ContainsInvalidChars(string value)
    {
        foreach (var ch in value)
        {
            if (ch is < ' ' and not '\t')
                return true;
        }

        return false;
    }
}

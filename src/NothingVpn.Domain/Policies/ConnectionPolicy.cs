namespace NothingVpn.Domain.Policies;

public static class ConnectionPolicy
{
    public const string ProxyMode = "proxy";
    public const string TunMode = "tun";
    public const string TunAppsMode = "tun_apps";

    public static string NormalizeMode(string? mode)
    {
        var value = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            TunMode => TunMode,
            TunAppsMode => TunAppsMode,
            _ => ProxyMode
        };
    }

    public static bool IsTunMode(string? mode)
    {
        var normalized = NormalizeMode(mode);
        return normalized is TunMode or TunAppsMode;
    }

    public static void EnsureTunAppsHasTargets(string? mode, IReadOnlyCollection<string> processPaths)
    {
        if (!string.Equals(NormalizeMode(mode), TunAppsMode, StringComparison.Ordinal))
            return;

        if (processPaths.Count == 0)
            throw new InvalidOperationException("В режиме TUN (выбранные приложения) нужно добавить хотя бы один .exe.");
    }
}


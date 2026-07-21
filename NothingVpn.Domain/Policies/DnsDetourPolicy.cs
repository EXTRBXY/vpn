namespace NothingVpn.Domain.Policies;

/// <summary>
/// Политика DNS detour: через proxy имеет смысл только там, где hijack-dns
/// перехватывает системный DNS без конфликта с моделью «часть приложений direct».
/// </summary>
public static class DnsDetourPolicy
{
    /// <summary>
    /// В tun_apps DNS через proxy ломает модель (весь DNS уходит в VPN)
    /// и часто даёт нерабочий bootstrap — запрещаем.
    /// </summary>
    public static bool AllowsProxyDetour(string? connectionMode)
    {
        var mode = (connectionMode ?? string.Empty).Trim().ToLowerInvariant();
        return mode is not "tun_apps";
    }

    public static string EffectiveDetour(string? connectionMode, string? requestedDetour)
    {
        var detour = (requestedDetour ?? string.Empty).Trim().ToLowerInvariant();
        if (detour != "proxy")
            return "direct";
        return AllowsProxyDetour(connectionMode) ? "proxy" : "direct";
    }
}

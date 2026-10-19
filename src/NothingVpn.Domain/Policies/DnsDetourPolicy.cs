using NothingVpn.Domain.Models;

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
    public static bool AllowsProxyDetour(ConnectionMode connectionMode) =>
        connectionMode != ConnectionMode.TunApps;

    public static DnsDetour EffectiveDetour(ConnectionMode connectionMode, DnsDetour requestedDetour) =>
        requestedDetour == DnsDetour.Proxy && AllowsProxyDetour(connectionMode)
            ? DnsDetour.Proxy
            : DnsDetour.Direct;
}

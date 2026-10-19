using NothingVpn.Domain.Models;

namespace NothingVpn.Presentation;

/// <summary>Подписи значений настроек для UI.</summary>
public static class SettingLabels
{
    public static string Detour(DnsDetour detour) => detour == DnsDetour.Proxy ? "Через VPN" : "Напрямую";

    public static string Stack(TunStack stack) => stack switch
    {
        TunStack.Mixed => "Смешанный",
        TunStack.Gvisor => "gVisor",
        _ => "Системный"
    };
}

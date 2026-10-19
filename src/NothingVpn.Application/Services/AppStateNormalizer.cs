using NothingVpn.Application.Mappers;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

/// <summary>
/// Единственная точка нормализации состояния: применяет Domain-политики ко всем полям настроек.
/// Вызывается при каждой записи настроек (SettingsService), перед запросом к ядру (VpnConnectionService) и в самом
/// ядре при приёме запроса (SingBoxVpnEngine);
/// генератор sing-box получает уже нормализованное состояние и сам ничего не нормализует.
/// Строковые значения разбирает при чтении state.json толерантный конвертер (Serialization), здесь enum'ы
/// только защищаются от неопределённых числовых значений.
/// </summary>
public static class AppStateNormalizer
{
    public static void Normalize(AppStateModel state, IPathPolicyPort pathPolicy)
    {
        state.Mode = ConnectionPolicy.Modes.Normalize(state.Mode);
        state.DnsDetour = DnsDetourPolicy.EffectiveDetour(state.Mode, state.DnsDetour);
        state.CloseBehavior = AppCloseBehaviors.Values.Normalize(state.CloseBehavior);
        state.SingBoxLogLevel = SingBoxLogLevelPolicy.Normalize(state.SingBoxLogLevel);
        state.TunAppProcessPaths = pathPolicy.NormalizeDistinctExePaths(state.TunAppProcessPaths).ToList();

        var dns = ConnectionSettingsMapper.ToDnsSettings(state);
        DnsPolicy.Normalize(dns);
        ConnectionSettingsMapper.ApplyDnsSettings(state, dns);

        var tun = ConnectionSettingsMapper.ToTunSettings(state);
        TunSettingsPolicy.Normalize(tun);
        ConnectionSettingsMapper.ApplyTunSettings(state, tun);

        var proxy = ConnectionSettingsMapper.ToProxySettings(state);
        ProxyConnectionPolicy.Normalize(proxy);
        ConnectionSettingsMapper.ApplyProxySettings(state, proxy);
    }
}

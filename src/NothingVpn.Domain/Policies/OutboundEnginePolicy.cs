using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

/// <summary>
/// Выбор движка выхода к серверу (ADR-0002). XHTTP есть только в Xray. REALITY в режиме «авто» тоже идёт через Xray:
/// официальный sing-box не отправляет ключ X25519MLKEM768, и новые серверы Xray отклоняют его рукопожатие.
/// </summary>
public static class OutboundEnginePolicy
{
    public const OutboundEngine DefaultPreference = OutboundEngine.Auto;

    private const string RealitySecurity = "reality";

    public static readonly SettingValueMap<OutboundEngine> Preferences = new(
        DefaultPreference,
        new Dictionary<OutboundEngine, string>
        {
            [OutboundEngine.Auto] = "auto",
            [OutboundEngine.SingBox] = "sing-box",
            [OutboundEngine.Xray] = "xray"
        });

    public static string DisplayName(OutboundEngine engine) => Preferences.Normalize(engine) switch
    {
        OutboundEngine.SingBox => "sing-box",
        OutboundEngine.Xray => "Xray",
        _ => "Авто"
    };

    /// <summary>Итоговый движок профиля: <see cref="OutboundEngine.SingBox"/> или <see cref="OutboundEngine.Xray"/>.</summary>
    public static OutboundEngine Resolve(OutboundEngine preference, string? transport, string? security)
    {
        var xrayOnly = XhttpPolicy.IsXhttp(transport);
        return Preferences.Normalize(preference) switch
        {
            OutboundEngine.SingBox when xrayOnly =>
                throw new ArgumentException("Транспорт XHTTP поддерживает только движок Xray."),
            OutboundEngine.SingBox => OutboundEngine.SingBox,
            OutboundEngine.Xray => OutboundEngine.Xray,
            _ => xrayOnly || string.Equals(security, RealitySecurity, StringComparison.OrdinalIgnoreCase)
                ? OutboundEngine.Xray
                : OutboundEngine.SingBox
        };
    }
}

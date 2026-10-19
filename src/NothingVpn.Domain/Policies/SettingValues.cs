using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

/// <summary>Каноническая строка значения настройки — та же, что в state.json и конфиге sing-box.</summary>
public static class SettingValues
{
    public static string ToValue(this ConnectionMode value) => ConnectionPolicy.Modes.ToValue(value);
    public static string ToValue(this DnsMode value) => DnsPolicy.Modes.ToValue(value);
    public static string ToValue(this DnsDetour value) => DnsPolicy.Detours.ToValue(value);
    public static string ToValue(this RuleSetAction value) => RuleSetPolicyValidator.Actions.ToValue(value);
    public static string ToValue(this TunStack value) => TunSettingsPolicy.Stacks.ToValue(value);
    public static string ToValue(this LogLevel value) => SingBoxLogLevelPolicy.Levels.ToValue(value);
}

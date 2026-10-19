using System.Text.Json;
using System.Text.Json.Serialization;
using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Serialization;

/// <summary>
/// Enum-настройка в state.json хранится канонической строкой (<see cref="SettingValueMap{T}"/>). Чтение толерантно:
/// регистр не важен, а неизвестная строка, null или значение другого типа дают значение по умолчанию поля, а не
/// исключение — иначе DpapiJsonFile счёл бы файл повреждённым и пользователь потерял бы все настройки.
/// </summary>
public abstract class SettingValueJsonConverter<T>(SettingValueMap<T> map) : JsonConverter<T>
    where T : struct, Enum
{
    public override bool HandleNull => true;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return map.Parse(reader.GetString());

        reader.Skip();
        return map.Fallback;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(map.ToValue(value));
}

public sealed class ConnectionModeJsonConverter() : SettingValueJsonConverter<ConnectionMode>(ConnectionPolicy.Modes);

public sealed class DnsModeJsonConverter() : SettingValueJsonConverter<DnsMode>(DnsPolicy.Modes);

public sealed class DnsDetourJsonConverter() : SettingValueJsonConverter<DnsDetour>(DnsPolicy.Detours);

public sealed class RuleSetActionJsonConverter() : SettingValueJsonConverter<RuleSetAction>(RuleSetPolicyValidator.Actions);

public sealed class TunStackJsonConverter() : SettingValueJsonConverter<TunStack>(TunSettingsPolicy.Stacks);

/// <summary>Неизвестный уровень читается как info, а не warn: так было до перехода на enum (SingBoxLogLevelPolicy).</summary>
public sealed class LogLevelJsonConverter() : SettingValueJsonConverter<LogLevel>(SingBoxLogLevelPolicy.Levels);

public sealed class AppCloseBehaviorJsonConverter() : SettingValueJsonConverter<AppCloseBehavior>(AppCloseBehaviors.Values);

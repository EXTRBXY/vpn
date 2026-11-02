using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NothingVpn.Application.Serialization;

/// <summary>Диапазон Xray: в ссылках встречается числом или строкой «N-M», хранится и пишется строкой.</summary>
public sealed class XrayRangeJsonConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
            _ => throw new JsonException("Ожидалось число или диапазон «N-M».")
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

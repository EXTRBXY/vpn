using System.Text.Json.Serialization;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Models;

/// <summary>Элемент state.json → userRuleSets. Имена JSON-свойств — схема на диске.</summary>
public sealed class UserRuleSetModel
{
    /// <summary>Стабильный tag для sing-box route.rule_set и matcher rule_set.</summary>
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Имя файла внутри каталога rulesets (обычно *.srs).</summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>direct|block.</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = RuleSetPolicyValidator.DirectAction;

    /// <summary>Встроенный каталог (например sing-geosite:category-ru). Пусто — пользовательский импорт.</summary>
    [JsonPropertyName("builtinId")]
    public string? BuiltinId { get; set; }

    /// <summary>ETag ответа при последней загрузке (для условных запросов).</summary>
    [JsonPropertyName("remoteEtag")]
    public string? RemoteEtag { get; set; }

    [JsonPropertyName("lastDownloadedUtc")]
    public DateTimeOffset? LastDownloadedUtc { get; set; }
}

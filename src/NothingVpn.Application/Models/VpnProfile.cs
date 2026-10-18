using System.Text.Json.Serialization;

namespace NothingVpn.Application.Models;

/// <summary>
/// Профиль VLESS. Это же схема элемента profiles.json: имена JSON-свойств зафиксированы явно.
/// </summary>
public sealed class VpnProfile
{
    /// <summary>VlessLinkParser.StableId; на нём держатся активный профиль, подписки и адреса TUN.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("subscriptionId")]
    public string? SubscriptionId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 443;

    /// <summary>tcp|ws|grpc.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "tcp";

    /// <summary>tls|reality|none.</summary>
    [JsonPropertyName("security")]
    public string Security { get; set; } = "tls";

    [JsonPropertyName("encryption")]
    public string Encryption { get; set; } = "none";

    [JsonPropertyName("sni")]
    public string? Sni { get; set; }

    [JsonPropertyName("alpn")]
    public List<string> Alpn { get; set; } = new();

    /// <summary>uTLS fingerprint (fp).</summary>
    [JsonPropertyName("fingerprint")]
    public string? Fingerprint { get; set; }

    [JsonPropertyName("flow")]
    public string? Flow { get; set; }

    /// <summary>Reality pbk.</summary>
    [JsonPropertyName("realityPublicKey")]
    public string? RealityPublicKey { get; set; }

    /// <summary>Reality sid.</summary>
    [JsonPropertyName("realityShortId")]
    public string? RealityShortId { get; set; }

    [JsonPropertyName("wsPath")]
    public string? WsPath { get; set; }

    [JsonPropertyName("wsHost")]
    public string? WsHost { get; set; }

    [JsonPropertyName("grpcServiceName")]
    public string? GrpcServiceName { get; set; }

    /// <summary>Параметры ссылки, которые клиент не разбирает (например ech). Ключи без учёта регистра.</summary>
    // Populate: при чтении словарь из инициализатора заполняется, а не заменяется — компаратор сохраняется.
    [JsonPropertyName("extraQuery")]
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> ExtraQuery { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

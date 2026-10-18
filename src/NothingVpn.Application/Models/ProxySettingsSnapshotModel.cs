using System.Text.Json.Serialization;

namespace NothingVpn.Application.Models;

/// <summary>Снимок настроек WinINET. Хранится в state.json → previousProxySettings.</summary>
public sealed class ProxySettingsSnapshotModel
{
    [JsonPropertyName("proxyEnable")]
    public bool ProxyEnable { get; set; }

    [JsonPropertyName("proxyServer")]
    public string? ProxyServer { get; set; }

    [JsonPropertyName("proxyOverride")]
    public string? ProxyOverride { get; set; }
}

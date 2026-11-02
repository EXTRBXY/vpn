using System.Text.Json;
using System.Text.Json.Serialization;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Infrastructure.Xray;

/// <summary>
/// Конфиг Xray для роли выхода (ADR-0002): один SOCKS5-вход на 127.0.0.1 с паролем и один VLESS-выход к серверу.
/// Маршрутизации, DNS и TUN нет — это делает sing-box. Состояние нормализовано ядром.
/// </summary>
internal static class XrayConfigGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(VpnProfile profile, AppStateModel state, LocalSocksUpstream inbound) =>
        JsonSerializer.Serialize(Build(profile, state, inbound), JsonOptions);

    public static XrayConfig Build(VpnProfile profile, AppStateModel state, LocalSocksUpstream inbound)
    {
        var network = ToNetwork(profile.Type);
        return new XrayConfig
        {
            Log = new XrayLog { LogLevel = ToLogLevel(state.SingBoxLogLevel) },
            Inbounds =
            [
                new XrayInbound
                {
                    Port = inbound.Port,
                    Settings = new XraySocksSettings
                    {
                        Accounts = [new XraySocksAccount { User = inbound.Username, Pass = inbound.Password }]
                    }
                }
            ],
            Outbounds =
            [
                new XrayOutbound
                {
                    Settings = new XrayVlessSettings
                    {
                        Address = profile.Host,
                        Port = profile.Port,
                        Id = profile.Uuid,
                        Flow = network == "raw" && profile.Security is "tls" or "reality" ? profile.Flow : null
                    },
                    StreamSettings = BuildStream(profile, state, network)
                }
            ]
        };
    }

    private static XrayStreamSettings BuildStream(VpnProfile p, AppStateModel state, string network) => new()
    {
        Network = network,
        Security = p.Security,
        TlsSettings = p.Security == "tls"
            ? new XrayTlsSettings
            {
                ServerName = p.Sni,
                Fingerprint = p.Fingerprint,
                Alpn = p.Alpn.Count > 0 ? p.Alpn : null
            }
            : null,
        RealitySettings = p.Security == "reality"
            ? new XrayRealitySettings
            {
                ServerName = p.Sni,
                Fingerprint = p.Fingerprint,
                PublicKey = p.RealityPublicKey,
                ShortId = p.RealityShortId
            }
            : null,
        WsSettings = network == "ws" ? new XrayWsSettings { Path = p.WsPath, Host = p.WsHost } : null,
        GrpcSettings = network == "grpc" ? new XrayGrpcSettings { ServiceName = p.GrpcServiceName } : null,
        XhttpSettings = network == XhttpPolicy.Transport
            ? new XrayXhttpSettings
            {
                Path = p.XhttpPath,
                Host = p.XhttpHost,
                Mode = XhttpPolicy.Modes.ToValue(p.XhttpMode),
                Extra = BuildExtra(p.XhttpExtra, state)
            }
            : null
    };

    /// <summary>XMUX из ссылки важнее; без него — XMUX по умолчанию из настроек, все поля явно.</summary>
    private static XhttpExtraModel BuildExtra(XhttpExtraModel? source, AppStateModel state)
    {
        var extra = source is null
            ? new XhttpExtraModel()
            : JsonSerializer.Deserialize<XhttpExtraModel>(JsonSerializer.Serialize(source))!;
        extra.Xmux ??= new XmuxModel
        {
            MaxConcurrency = state.XhttpXmuxMaxConcurrency,
            MaxConnections = state.XhttpXmuxMaxConnections,
            CMaxReuseTimes = state.XhttpXmuxCMaxReuseTimes,
            HMaxRequestTimes = state.XhttpXmuxHMaxRequestTimes,
            HMaxReusableSecs = state.XhttpXmuxHMaxReusableSecs
        };
        return extra;
    }

    private static string ToNetwork(string type) => type switch
    {
        "tcp" => "raw",
        "ws" => "ws",
        "grpc" => "grpc",
        XhttpPolicy.Transport => XhttpPolicy.Transport,
        _ => throw new ArgumentException("Unsupported transport type.")
    };

    private static string ToLogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "debug",
        LogLevel.Info => "info",
        LogLevel.Warn => "warning",
        _ => "error"
    };
}

internal sealed class XrayConfig
{
    public XrayLog Log { get; set; } = new();
    public List<XrayInbound> Inbounds { get; set; } = new();
    public List<XrayOutbound> Outbounds { get; set; } = new();
}

internal sealed class XrayLog
{
    [JsonPropertyName("loglevel")]
    public string LogLevel { get; set; } = "warning";

    public string Access { get; set; } = "none";
}

internal sealed class XrayInbound
{
    public string Tag { get; set; } = "socks-in";
    public string Listen { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public string Protocol { get; set; } = "socks";
    public XraySocksSettings Settings { get; set; } = new();
}

internal sealed class XraySocksSettings
{
    public string Auth { get; set; } = "password";
    public List<XraySocksAccount> Accounts { get; set; } = new();
    public bool Udp { get; set; } = true;
    public string Ip { get; set; } = "127.0.0.1";
}

internal sealed class XraySocksAccount
{
    public string User { get; set; } = "";
    public string Pass { get; set; } = "";
}

internal sealed class XrayOutbound
{
    public string Tag { get; set; } = "proxy";
    public string Protocol { get; set; } = "vless";
    public XrayVlessSettings Settings { get; set; } = new();
    public XrayStreamSettings StreamSettings { get; set; } = new();
}

internal sealed class XrayVlessSettings
{
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public string Id { get; set; } = "";
    public string Encryption { get; set; } = "none";
    public string? Flow { get; set; }
}

internal sealed class XrayStreamSettings
{
    public string Network { get; set; } = "raw";
    public string Security { get; set; } = "none";
    public XrayTlsSettings? TlsSettings { get; set; }
    public XrayRealitySettings? RealitySettings { get; set; }
    public XrayWsSettings? WsSettings { get; set; }
    public XrayGrpcSettings? GrpcSettings { get; set; }
    public XrayXhttpSettings? XhttpSettings { get; set; }
}

internal sealed class XrayTlsSettings
{
    public string? ServerName { get; set; }
    public string? Fingerprint { get; set; }
    public List<string>? Alpn { get; set; }
}

internal sealed class XrayRealitySettings
{
    public string? ServerName { get; set; }
    public string? Fingerprint { get; set; }
    public string? PublicKey { get; set; }
    public string? ShortId { get; set; }
}

internal sealed class XrayWsSettings
{
    public string? Path { get; set; }
    public string? Host { get; set; }
}

internal sealed class XrayGrpcSettings
{
    public string? ServiceName { get; set; }
}

internal sealed class XrayXhttpSettings
{
    public string? Path { get; set; }
    public string? Host { get; set; }
    public string Mode { get; set; } = "auto";
    public XhttpExtraModel? Extra { get; set; }
}

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Infrastructure.SingBox;

/// <summary>
/// NothingX: транспорт XHTTP есть только в Xray-core, поэтому для профилей type=xhttp рядом с sing-box запускается
/// <see cref="ExeName"/>. sing-box остаётся входом (mixed/TUN) и отправляет трафик в локальный SOCKS5 Xray
/// (127.0.0.1, случайный порт, одноразовые логин и пароль), а Xray идёт к серверу по VLESS + XHTTP.
/// </summary>
internal static class XrayConfigGenerator
{
    public const string ExeName = "nothingx-xray.exe";
    public const string ConfigSuffix = ".xray.json";

    public static bool IsRequired(VpnProfile profile) =>
        string.Equals(profile.Type, "xhttp", StringComparison.OrdinalIgnoreCase);

    public static string ConfigPathFor(string singBoxConfigPath) =>
        Path.ChangeExtension(singBoxConfigPath, null) + ConfigSuffix;

    public static XraySidecar CreateSidecar() => new(
        Port: FindFreeLoopbackPort(),
        Username: RandomToken(12),
        Password: RandomToken(24));

    public static string Build(VpnProfile profile, AppStateModel state, XraySidecar sidecar)
    {
        if (!IsRequired(profile))
            throw new ArgumentException("Xray нужен только для профилей xhttp.");

        var user = new JsonObject
        {
            ["id"] = profile.Uuid,
            ["encryption"] = "none"
        };

        var stream = new JsonObject
        {
            ["network"] = "xhttp",
            ["security"] = profile.Security,
            ["xhttpSettings"] = BuildXhttpSettings(profile)
        };

        var serverName = string.IsNullOrWhiteSpace(profile.Sni) ? profile.Host : profile.Sni;
        var fingerprint = string.IsNullOrWhiteSpace(profile.Fingerprint) ? "chrome" : profile.Fingerprint;
        switch (profile.Security)
        {
            case "reality":
                var reality = new JsonObject
                {
                    ["serverName"] = serverName,
                    ["fingerprint"] = fingerprint,
                    ["publicKey"] = profile.RealityPublicKey,
                    ["shortId"] = profile.RealityShortId
                };
                if (profile.ExtraQuery.TryGetValue("spx", out var spiderX) && !string.IsNullOrWhiteSpace(spiderX))
                    reality["spiderX"] = spiderX;
                stream["realitySettings"] = reality;
                break;
            case "tls":
                var tls = new JsonObject
                {
                    ["serverName"] = serverName,
                    ["fingerprint"] = fingerprint
                };
                if (profile.Alpn.Count > 0)
                    tls["alpn"] = new JsonArray(profile.Alpn.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray());
                stream["tlsSettings"] = tls;
                break;
            case "none":
                break;
            default:
                throw new ArgumentException("Unsupported security.");
        }

        var config = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["loglevel"] = MapLogLevel(state.SingBoxLogLevel),
                ["access"] = "none"
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "socks-in",
                    ["listen"] = "127.0.0.1",
                    ["port"] = sidecar.Port,
                    ["protocol"] = "socks",
                    ["settings"] = new JsonObject
                    {
                        ["auth"] = "password",
                        ["accounts"] = new JsonArray
                        {
                            new JsonObject { ["user"] = sidecar.Username, ["pass"] = sidecar.Password }
                        },
                        ["udp"] = true,
                        ["ip"] = "127.0.0.1"
                    }
                }
            },
            ["outbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "proxy",
                    ["protocol"] = "vless",
                    ["settings"] = new JsonObject
                    {
                        ["vnext"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["address"] = profile.Host,
                                ["port"] = profile.Port,
                                ["users"] = new JsonArray { user }
                            }
                        }
                    },
                    ["streamSettings"] = stream
                }
            }
        };

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject BuildXhttpSettings(VpnProfile profile)
    {
        var settings = new JsonObject
        {
            ["path"] = string.IsNullOrWhiteSpace(profile.WsPath) ? "/" : profile.WsPath,
            ["mode"] = profile.XhttpMode ?? "auto"
        };
        if (!string.IsNullOrWhiteSpace(profile.WsHost))
            settings["host"] = profile.WsHost;

        var extra = string.IsNullOrWhiteSpace(profile.XhttpExtra)
            ? new JsonObject()
            : JsonNode.Parse(profile.XhttpExtra)!.AsObject();
        // Без явного xmux клиент Xray почти на каждый запрос открывает новое TCP+TLS-соединение, а частые новые
        // рукопожатия к одному IP ТСПУ режет (проверено 02.11.2026). Держим запросы в нескольких долгих соединениях.
        if (!extra.ContainsKey("xmux"))
        {
            extra["xmux"] = new JsonObject
            {
                ["maxConcurrency"] = "16-32",
                ["cMaxReuseTimes"] = 0,
                ["hMaxRequestTimes"] = "600-900",
                ["hMaxReusableSecs"] = "1800-3000"
            };
        }
        settings["extra"] = extra;
        return settings;
    }

    // Xray на info пишет каждое соединение с доменом — в журнал клиента это не нужно.
    private static string MapLogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "debug",
        LogLevel.Info or LogLevel.Warn => "warning",
        _ => "error"
    };

    private static int FindFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string RandomToken(int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return string.Create(length, alphabet, static (span, a) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = a[RandomNumberGenerator.GetInt32(a.Length)];
        });
    }

    /// <summary>Порт локального SOCKS5 из готового конфига Xray (для ожидания запуска).</summary>
    public static int? TryReadSocksPort(string xrayConfigPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(xrayConfigPath));
            return doc.RootElement.GetProperty("inbounds")[0].GetProperty("port").GetInt32();
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record XraySidecar(int Port, string Username, string Password);

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NothingVpn.Application.Models;

namespace NothingVpn.Infrastructure.Profile;

internal static class VlessLinkParser
{
    public static VpnProfile Parse(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            throw new ArgumentException("Empty link.", nameof(link));

        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid URI.");

        if (!string.Equals(uri.Scheme, "vless", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only vless:// links are supported.");

        // vless://uuid@host:port?...#name
        var userInfo = uri.UserInfo;
        if (string.IsNullOrWhiteSpace(userInfo))
            throw new ArgumentException("Missing UUID in userinfo.");

        if (!Guid.TryParse(userInfo, out var guid))
            throw new ArgumentException("Invalid UUID format.");

        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("Missing host.");

        var port = uri.Port;
        if (port <= 0 || port > 65535)
            throw new ArgumentException("Invalid port.");

        var name = string.IsNullOrWhiteSpace(uri.Fragment) ? $"{uri.Host}:{port}" : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

        var query = ParseQuery(uri.Query);

        var profile = new VpnProfile
        {
            Uuid = guid.ToString(),
            Host = uri.Host,
            Port = port,
            Name = SanitizeName(name),
        };

        profile.Type = Get(query, "type") ?? "tcp";
        profile.Security = Get(query, "security") ?? "tls";
        profile.Encryption = Get(query, "encryption") ?? "none";

        profile.Fingerprint = Get(query, "fp");
        profile.Sni = Get(query, "sni") ?? Get(query, "serverName") ?? Get(query, "servername");
        profile.Flow = Get(query, "flow");

        var alpn = Get(query, "alpn");
        if (!string.IsNullOrWhiteSpace(alpn))
        {
            // Some exporters use comma-separated; most use single value.
            profile.Alpn = alpn.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        // Reality
        profile.RealityPublicKey = Get(query, "pbk");
        profile.RealityShortId = Get(query, "sid");

        // Transports (optional)
        profile.WsPath = Get(query, "path");
        profile.WsHost = Get(query, "host");
        profile.GrpcServiceName = Get(query, "serviceName") ?? Get(query, "servicename");
        profile.XhttpMode = Get(query, "mode");
        profile.XhttpExtra = Get(query, "extra");

        // Keep extra params we don't map (e.g. ech)
        foreach (var kv in query)
        {
            if (!IsKnownKey(kv.Key))
                profile.ExtraQuery[kv.Key] = kv.Value;
        }

        ValidateProfile(profile);
        profile.Id = StableId(profile);
        return profile;
    }

    private static void ValidateProfile(VpnProfile p)
    {
        if (!Guid.TryParse(p.Uuid, out _)) throw new ArgumentException("UUID invalid.");
        if (string.IsNullOrWhiteSpace(p.Host)) throw new ArgumentException("Host is required.");
        if (p.Port <= 0 || p.Port > 65535) throw new ArgumentException("Port invalid.");

        p.Type = NormalizeLower(p.Type, "type");
        p.Security = NormalizeLower(p.Security, "security");
        p.Encryption = NormalizeLower(p.Encryption, "encryption");
        p.Flow = string.IsNullOrWhiteSpace(p.Flow) ? null : p.Flow.Trim();
        p.Fingerprint = string.IsNullOrWhiteSpace(p.Fingerprint) ? null : p.Fingerprint.Trim();
        p.Sni = string.IsNullOrWhiteSpace(p.Sni) ? null : p.Sni.Trim();

        if (p.Encryption is not "none")
            throw new ArgumentException("Only encryption=none is supported for VLESS links.");

        if (p.Security is not ("tls" or "reality" or "none"))
            throw new ArgumentException("Unsupported security. Expected tls|reality|none.");

        if (p.Type == "splithttp")
            p.Type = "xhttp";

        if (p.Type is not ("tcp" or "ws" or "grpc" or "xhttp"))
            throw new ArgumentException("Unsupported type. Expected tcp|ws|grpc|xhttp.");

        if (p.Type == "xhttp")
        {
            p.XhttpMode = string.IsNullOrWhiteSpace(p.XhttpMode) ? null : p.XhttpMode.Trim().ToLowerInvariant();
            if (p.XhttpMode is not (null or "auto" or "packet-up" or "stream-up" or "stream-one"))
                throw new ArgumentException("Unsupported xhttp mode. Expected auto|packet-up|stream-up|stream-one.");
            p.XhttpExtra = string.IsNullOrWhiteSpace(p.XhttpExtra) ? null : p.XhttpExtra.Trim();
            if (p.XhttpExtra is not null)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(p.XhttpExtra);
                    if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                        throw new ArgumentException("xhttp extra must be a JSON object.");
                }
                catch (System.Text.Json.JsonException)
                {
                    throw new ArgumentException("xhttp extra is not valid JSON.");
                }
            }
            // XTLS Vision работает только поверх RAW/TCP; с XHTTP flow не используется.
            p.Flow = null;
        }
        else
        {
            p.XhttpMode = null;
            p.XhttpExtra = null;
        }

        if (p.Security == "reality")
        {
            if (string.IsNullOrWhiteSpace(p.RealityPublicKey)) throw new ArgumentException("Reality requires pbk.");
            if (string.IsNullOrWhiteSpace(p.RealityShortId)) throw new ArgumentException("Reality requires sid.");
            if (string.IsNullOrWhiteSpace(p.Sni)) throw new ArgumentException("Reality requires sni.");
        }
    }

    private static string StableId(VpnProfile p)
    {
        var key = $"{p.Uuid}|{p.Host}|{p.Port}|{p.Type}|{p.Security}|{p.Sni}|{p.Flow}|{p.RealityPublicKey}|{p.RealityShortId}|{p.WsPath}|{p.WsHost}|{p.GrpcServiceName}";
        if (p.Type == "xhttp")
            key += $"|xhttp|{p.XhttpMode}|{p.XhttpExtra}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static string? Get(Dictionary<string, string> q, string key)
        => q.TryGetValue(key, out var v) ? v : null;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return dict;
        var q = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(q)) return dict;

        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0)
            {
                dict[Uri.UnescapeDataString(part)] = "";
                continue;
            }
            var k = Uri.UnescapeDataString(part[..idx]);
            var v = Uri.UnescapeDataString(part[(idx + 1)..]);
            dict[k] = v;
        }
        return dict;
    }

    private static string NormalizeLower(string value, string name)
    {
        var v = value?.Trim();
        if (string.IsNullOrWhiteSpace(v)) throw new ArgumentException($"{name} is empty.");
        return v.ToLowerInvariant();
    }

    private static bool IsKnownKey(string key)
    {
        return key.Equals("type", StringComparison.OrdinalIgnoreCase)
            || key.Equals("security", StringComparison.OrdinalIgnoreCase)
            || key.Equals("encryption", StringComparison.OrdinalIgnoreCase)
            || key.Equals("fp", StringComparison.OrdinalIgnoreCase)
            || key.Equals("alpn", StringComparison.OrdinalIgnoreCase)
            || key.Equals("sni", StringComparison.OrdinalIgnoreCase)
            || key.Equals("serverName", StringComparison.OrdinalIgnoreCase)
            || key.Equals("servername", StringComparison.OrdinalIgnoreCase)
            || key.Equals("flow", StringComparison.OrdinalIgnoreCase)
            || key.Equals("pbk", StringComparison.OrdinalIgnoreCase)
            || key.Equals("sid", StringComparison.OrdinalIgnoreCase)
            || key.Equals("path", StringComparison.OrdinalIgnoreCase)
            || key.Equals("host", StringComparison.OrdinalIgnoreCase)
            || key.Equals("serviceName", StringComparison.OrdinalIgnoreCase)
            || key.Equals("servicename", StringComparison.OrdinalIgnoreCase)
            || key.Equals("mode", StringComparison.OrdinalIgnoreCase)
            || key.Equals("extra", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeName(string name)
    {
        var n = name.Trim();
        if (n.Length == 0) return "Unnamed";
        return n.Length > 64 ? n[..64] : n;
    }
}


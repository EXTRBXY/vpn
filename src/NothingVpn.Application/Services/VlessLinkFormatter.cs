using System.Text;
using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public static class VlessLinkFormatter
{
    public static string Build(VpnProfile profile)
    {
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Uuid))
            throw new ArgumentException("UUID is required.", nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Host))
            throw new ArgumentException("Host is required.", nameof(profile));
        if (profile.Port <= 0 || profile.Port > 65535)
            throw new ArgumentOutOfRangeException(nameof(profile.Port), "Port is invalid.");

        // vless://uuid@host:port?...#name
        // Uri.Fragment is parsed as name; Uri.UnescapeDataString is applied by the parser.
        var name = string.IsNullOrWhiteSpace(profile.Name)
            ? $"{profile.Host}:{profile.Port}"
            : profile.Name.Trim();

        var host = profile.Host.Trim();
        if (host.Contains(':') && !host.StartsWith("[", StringComparison.Ordinal))
            host = $"[{host}]";

        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // These keys are recognized by NothingVpn.Tray/Internal/Profile/VlessLinkParser.cs
        q["type"] = string.IsNullOrWhiteSpace(profile.Type) ? "tcp" : profile.Type.Trim();
        q["security"] = string.IsNullOrWhiteSpace(profile.Security) ? "tls" : profile.Security.Trim();
        q["encryption"] = string.IsNullOrWhiteSpace(profile.Encryption) ? "none" : profile.Encryption.Trim();

        if (!string.IsNullOrWhiteSpace(profile.Sni))
            q["sni"] = profile.Sni.Trim();
        if (profile.Alpn is { Count: > 0 })
            q["alpn"] = string.Join(",", profile.Alpn);
        if (!string.IsNullOrWhiteSpace(profile.Fingerprint))
            q["fp"] = profile.Fingerprint.Trim();
        if (!string.IsNullOrWhiteSpace(profile.Flow))
            q["flow"] = profile.Flow.Trim();

        if (!string.IsNullOrWhiteSpace(profile.RealityPublicKey))
            q["pbk"] = profile.RealityPublicKey.Trim();
        if (!string.IsNullOrWhiteSpace(profile.RealityShortId))
            q["sid"] = profile.RealityShortId.Trim();

        if (!string.IsNullOrWhiteSpace(profile.WsPath))
            q["path"] = profile.WsPath.Trim();
        if (!string.IsNullOrWhiteSpace(profile.WsHost))
            q["host"] = profile.WsHost.Trim();
        if (!string.IsNullOrWhiteSpace(profile.GrpcServiceName))
            q["serviceName"] = profile.GrpcServiceName.Trim();

        // Preserve unknown parameters from imports.
        foreach (var kv in profile.ExtraQuery)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
                continue;
            if (q.ContainsKey(kv.Key))
                continue;
            q[kv.Key] = kv.Value;
        }

        static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

        var query = new StringBuilder();
        var first = true;
        foreach (var key in q.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var value = q[key];
            if (value is null) continue;

            if (!first)
                query.Append('&');
            first = false;
            query.Append(key);
            query.Append('=');
            query.Append(Escape(value));
        }

        var fragment = Uri.EscapeDataString(name);

        var url = $"vless://{profile.Uuid.Trim()}@{host}:{profile.Port}";
        if (!first)
            url += $"?{query}";
        url += $"#{fragment}";
        return url;
    }
}


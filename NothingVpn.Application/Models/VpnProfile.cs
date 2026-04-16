namespace NothingVpn.Application.Models;

public sealed class VpnProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 443;
    public string Type { get; set; } = "tcp";
    public string Security { get; set; } = "tls";
    public string Encryption { get; set; } = "none";
    public string? Sni { get; set; }
    public List<string> Alpn { get; set; } = new();
    public string? Fingerprint { get; set; }
    public string? Flow { get; set; }
    public string? RealityPublicKey { get; set; }
    public string? RealityShortId { get; set; }
    public string? WsPath { get; set; }
    public string? WsHost { get; set; }
    public string? GrpcServiceName { get; set; }
    public Dictionary<string, string> ExtraQuery { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}


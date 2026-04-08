namespace NothingVpn.Tray.Internal.Profile;

internal sealed class VlessProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Unnamed";

    public string Uuid { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 443;

    public string Type { get; set; } = "tcp"; // tcp|ws|grpc
    public string Security { get; set; } = "tls"; // tls|reality|none
    public string Encryption { get; set; } = "none";

    public string? Sni { get; set; }
    public List<string> Alpn { get; set; } = new();
    public string? Fingerprint { get; set; } // fp
    public string? Flow { get; set; } // flow (e.g. xtls-rprx-vision)

    // Reality fields
    public string? RealityPublicKey { get; set; } // pbk
    public string? RealityShortId { get; set; } // sid

    // Transport fields (optional for future)
    public string? WsPath { get; set; }
    public string? WsHost { get; set; }
    public string? GrpcServiceName { get; set; }

    // Raw/ignored params for diagnostics
    public Dictionary<string, string> ExtraQuery { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}


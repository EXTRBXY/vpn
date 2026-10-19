namespace NothingVpn.Domain.Models;

/// <summary>Готовые параметры DoH; <see cref="Custom"/> — параметры не совпадают ни с одним набором.</summary>
public enum DnsPreset
{
    Custom,
    Cloudflare,
    Google,
    Quad9,
    AdGuard
}

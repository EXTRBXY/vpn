namespace NothingVpn.Domain.Models;

/// <summary>Режим подключения. В state.json и аргументе --mode: proxy | tun | tun_apps.</summary>
public enum ConnectionMode
{
    Proxy,
    Tun,
    TunApps
}

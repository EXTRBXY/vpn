namespace NothingVpn.Domain.Models;

/// <summary>Маршрут DoH-запросов. В state.json: direct | proxy.</summary>
public enum DnsDetour
{
    Direct,
    Proxy
}

using NothingVpn.Domain.Policies;

namespace NothingVpn.Domain.Models;

public sealed class ProxyConnectionSettings
{
    public string ProxyOverride { get; set; } = ProxyConnectionPolicy.DefaultProxyOverride;
}

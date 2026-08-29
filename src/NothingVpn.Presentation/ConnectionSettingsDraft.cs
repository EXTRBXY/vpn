using NothingVpn.Domain.Models;

namespace NothingVpn.Presentation;

public sealed record ConnectionSettingsDraft(
    ProxyConnectionSettings Proxy,
    TunSettings Tun,
    DnsSettings Dns);

using NothingVpn.Domain.Models;
using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public sealed record ConnectionSettingsDraft(
    ProxyConnectionSettings Proxy,
    TunSettings Tun,
    DnsSettings Dns)
{
    public IReadOnlyList<string>? TunAppPaths { get; init; }
    public IReadOnlyList<UserRuleSetModel>? RuleSets { get; init; }
    public string? LogLevel { get; init; }
    public string? CloseBehavior { get; init; }
}

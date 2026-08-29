namespace NothingVpn.Domain.Models;

public sealed class SubscriptionUserInfo
{
    public long Upload { get; init; }
    public long Download { get; init; }
    public long Total { get; init; }
    public DateTimeOffset? ExpireUtc { get; init; }
}

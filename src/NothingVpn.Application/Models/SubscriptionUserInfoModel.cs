namespace NothingVpn.Application.Models;

public sealed class SubscriptionUserInfoModel
{
    public long Upload { get; set; }
    public long Download { get; set; }
    public long Total { get; set; }
    public DateTimeOffset? ExpireUtc { get; set; }
}

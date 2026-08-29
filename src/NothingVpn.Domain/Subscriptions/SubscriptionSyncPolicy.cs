namespace NothingVpn.Domain.Subscriptions;

public static class SubscriptionSyncPolicy
{
    public const int DefaultUpdateIntervalHours = 24;
    public const int MinUpdateIntervalHours = 1;
    public const int MaxUpdateIntervalHours = 24 * 30;
}

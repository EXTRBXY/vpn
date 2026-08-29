namespace NothingVpn.Application.Models;

public sealed class SubscriptionModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastSyncUtc { get; set; }
    public string? LastError { get; set; }
    public int UpdateIntervalHours { get; set; } = 24;
    public SubscriptionUserInfoModel UserInfo { get; set; } = new();
}

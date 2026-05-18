namespace NothingVpn.Application.Models;

public sealed class SubscriptionRefreshResult
{
    public string SubscriptionId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Removed { get; init; }
    public int SkippedNonVless { get; init; }
    public IReadOnlyList<string> ParseErrors { get; init; } = Array.Empty<string>();
    public bool ActiveProfileCleared { get; init; }
}

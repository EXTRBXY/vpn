namespace NothingVpn.Application.Models;

public sealed class SubscriptionFetchResult
{
    public bool Success { get; init; }
    public int? StatusCode { get; init; }
    public string Body { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string? Error { get; init; }
}

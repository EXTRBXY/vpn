namespace NothingVpn.Domain.Models;

public sealed class UserRuleSetEntry
{
    public string Tag { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public string Action { get; init; } = "direct";
}


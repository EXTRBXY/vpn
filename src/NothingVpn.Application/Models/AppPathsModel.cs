namespace NothingVpn.Application.Models;

public sealed class AppPathsModel
{
    public string BaseDir { get; init; } = string.Empty;
    public string ConfigsDir { get; init; } = string.Empty;
    public string RuleSetsDir { get; init; } = string.Empty;
    public string LogsDir { get; init; } = string.Empty;
    public string ProfilesJsonPath { get; init; } = string.Empty;
    public string SubscriptionsJsonPath { get; init; } = string.Empty;
    public string StateJsonPath { get; init; } = string.Empty;
}


namespace NothingVpn.Application.Models;

public sealed class StorageIssueModel
{
    public required string Path { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset OccurredUtc { get; init; }
    public bool RecoveredFromBackup { get; init; }
}

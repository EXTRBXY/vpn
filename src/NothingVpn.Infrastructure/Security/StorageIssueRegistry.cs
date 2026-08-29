using System.Collections.Concurrent;
using NothingVpn.Application.Models;

namespace NothingVpn.Infrastructure.Security;

internal static class StorageIssueRegistry
{
    private static readonly ConcurrentQueue<StorageIssueModel> Issues = new();

    public static void Report(string path, bool recoveredFromBackup, string message)
    {
        Issues.Enqueue(new StorageIssueModel
        {
            Path = path,
            Message = message,
            OccurredUtc = DateTimeOffset.UtcNow,
            RecoveredFromBackup = recoveredFromBackup
        });
    }

    public static IReadOnlyList<StorageIssueModel> Drain()
    {
        var result = new List<StorageIssueModel>();
        while (Issues.TryDequeue(out var issue))
            result.Add(issue);
        return result;
    }
}

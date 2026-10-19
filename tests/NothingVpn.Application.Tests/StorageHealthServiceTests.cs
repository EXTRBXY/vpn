using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Ports;

namespace NothingVpn.Application.Tests;

public sealed class StorageHealthServiceTests
{
    [Fact]
    public void DrainIssues_WritesEachIssueToLog_WithFileNameOnly()
    {
        var log = new LogPort(new InMemoryLogStore());
        var port = new FakeStorageHealthPort(
            new StorageIssueModel
            {
                Path = @"C:\Users\someone\AppData\Local\NothingVpn.Tray\state.json",
                Message = "Основной файл отсутствовал и был восстановлен из резервной копии.",
                OccurredUtc = DateTimeOffset.UtcNow,
                RecoveredFromBackup = true
            },
            new StorageIssueModel
            {
                Path = @"C:\Users\someone\AppData\Local\NothingVpn.Tray\profiles.json",
                Message = "Основной файл и резервную копию не удалось прочитать.",
                OccurredUtc = DateTimeOffset.UtcNow
            });

        var issues = new StorageHealthService(port, log).DrainIssues();

        Assert.Equal(2, issues.Count);
        var text = log.SnapshotText(LogLevel.Trace);
        Assert.Contains("[WARN] [app/storage] Основной файл отсутствовал и был восстановлен из резервной копии. Файл: state.json.", text);
        Assert.Contains("[ERROR] [app/storage] Основной файл и резервную копию не удалось прочитать. Файл: profiles.json.", text);
        Assert.DoesNotContain("someone", text);
    }

    private sealed class FakeStorageHealthPort(params StorageIssueModel[] issues) : IStorageHealthPort
    {
        public IReadOnlyList<StorageIssueModel> DrainIssues() => issues;
    }
}

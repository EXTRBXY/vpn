using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Ports;

namespace NothingVpn.Application.Tests;

public sealed class PersistentLogArchiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nothingvpn-log-tests-" + Guid.NewGuid());
    private readonly List<PersistentLogArchive> _archives = new();

    [Fact]
    public void OptIn_SurvivesClearAndRestart_AndDoesNotPersistWhenDisabled()
    {
        var archive = Open();
        var store = new InMemoryLogStore();
        var port = new LogPort(store, archive);
        port.Append(LogLevel.Info, "app", "before enabling");
        archive.Flush();
        Assert.False(Directory.Exists(_dir));
        port.SetPersistentLogging(true);
        port.Append(LogLevel.Info, "app", "first connection uuid=4eeabfe7-2bbd-4988-b11c-1d9fcf5745a1");
        port.Clear();
        var occurred = new DateTimeOffset(2026, 11, 20, 6, 5, 18, TimeSpan.Zero);
        port.AppendAt(LogLevel.Warn, "sing-box", "second connection", occurred);
        archive.Flush();

        var restarted = Open();
        Assert.True(restarted.Enabled);
        Assert.True(File.Exists(Path.Combine(_dir, "persistent-logging.enabled")));
        restarted.Append(new LogLine(DateTimeOffset.UtcNow, LogLevel.Info, "xray", "after\nrestart"));
        restarted.Flush();
        var text = File.ReadAllText(Path.Combine(_dir, "developer.log"));
        var first = text.IndexOf("first connection", StringComparison.Ordinal);
        var second = text.IndexOf("second connection", StringComparison.Ordinal);
        var after = text.IndexOf("after restart", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < second && second < after);
        Assert.Contains("[WARN]", text, StringComparison.Ordinal);
        Assert.Contains("2026-11-20T06:05:18.0000000+00:00", text, StringComparison.Ordinal);
        Assert.DoesNotContain("after\nrestart", text, StringComparison.Ordinal);
        Assert.DoesNotContain("4eeabfe7-2bbd-4988-b11c-1d9fcf5745a1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("before enabling", text, StringComparison.Ordinal);

        restarted.SetEnabled(false);
        restarted.Append(new LogLine(DateTimeOffset.UtcNow, LogLevel.Info, "xray", "not persisted"));
        restarted.Flush();
        Assert.False(File.Exists(Path.Combine(_dir, "persistent-logging.enabled")));
        Assert.False(Open().Enabled);
        Assert.DoesNotContain("not persisted", File.ReadAllText(Path.Combine(_dir, "developer.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void Archive_RotatesAndLimitsTotalSize()
    {
        var archive = Open();
        archive.SetEnabled(true);
        var text = new string('x', 90_000);
        for (var i = 0; i < 65; i++)
            archive.Append(new LogLine(DateTimeOffset.UtcNow, LogLevel.Info, "test", "#" + i + " " + text));
        archive.Flush();
        var files = Directory.GetFiles(_dir, "*.log");
        Assert.Equal(5, files.Length);
        Assert.All(files, path => Assert.True(new FileInfo(path).Length <= 1_000_000));
        var all = string.Concat(files.Select(File.ReadAllText));
        Assert.Contains("#60 ", all, StringComparison.Ordinal);
        Assert.Contains("#64 ", all, StringComparison.Ordinal);
    }

    [Fact]
    public void ParallelAppend_KeepsEveryLine()
    {
        var archive = Open();
        archive.SetEnabled(true);
        const int count = 100;
        Parallel.For(0, count, i => archive.Append(new LogLine(DateTimeOffset.UnixEpoch, LogLevel.Info, "t", "n=" + i + ";")));
        archive.Flush();
        var text = File.ReadAllText(Path.Combine(_dir, "developer.log"));
        for (var i = 0; i < count; i++)
            Assert.Contains("n=" + i + ";", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteFailure_IsReportedWithoutBreakingLogging()
    {
        var archive = Open();
        var port = new LogPort(new InMemoryLogStore(), archive);
        archive.SetEnabled(true);
        Directory.CreateDirectory(Path.Combine(_dir, "developer.log"));
        port.Append(LogLevel.Error, "test", "failure must not break app");
        archive.Flush();
        Assert.NotNull(archive.Error);
        Assert.Contains("failure must not break app", port.SnapshotText(LogLevel.Trace), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var archive in _archives)
            archive.Dispose();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private PersistentLogArchive Open()
    {
        var archive = new PersistentLogArchive(_dir);
        _archives.Add(archive);
        return archive;
    }
}

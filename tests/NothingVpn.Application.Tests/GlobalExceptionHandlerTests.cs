using NothingVpn.Application.Ports;
using NothingVpn.Desktop.Wpf;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Ports;

namespace NothingVpn.Application.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public void UiException_IsLoggedAndShown_AndAppContinues()
    {
        var log = new FakeLog();
        var shown = new List<string>();
        var stops = 0;
        var handler = new GlobalExceptionHandler(log, shown.Add, () => { stops++; return Task.CompletedTask; });

        var handled = handler.HandleUiException(new InvalidOperationException("boom"));

        Assert.True(handled);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("InvalidOperationException: boom", entry.Message);
        Assert.Contains("boom", Assert.Single(shown));
        Assert.Equal(0, stops);
    }

    [Fact]
    public void UiException_WhileMessageIsShown_IsOnlyLogged()
    {
        var log = new FakeLog();
        var shown = new List<string>();
        GlobalExceptionHandler? handler = null;
        handler = new GlobalExceptionHandler(log, message =>
        {
            shown.Add(message);
            Assert.True(handler!.HandleUiException(new InvalidOperationException("nested")));
        }, () => Task.CompletedTask);

        Assert.True(handler.HandleUiException(new InvalidOperationException("first")));
        Assert.True(handler.HandleUiException(new InvalidOperationException("second")));

        Assert.Equal(2, shown.Count);
        Assert.Equal(4, log.Entries.Count);
    }

    [Fact]
    public void UiException_FailureToShowMessage_IsLogged()
    {
        var log = new FakeLog();
        var handler = new GlobalExceptionHandler(log, _ => throw new InvalidOperationException("no window"), () => Task.CompletedTask);

        Assert.True(handler.HandleUiException(new InvalidOperationException("boom")));

        Assert.Equal(2, log.Entries.Count);
        Assert.Contains("no window", log.Entries[1].Message);
    }

    [Fact]
    public void UnrecoverableUiException_IsFatal_AndStopsVpn()
    {
        var log = new FakeLog();
        var shown = new List<string>();
        var stops = 0;
        var handler = new GlobalExceptionHandler(log, shown.Add, () => { stops++; return Task.CompletedTask; });

        var handled = handler.HandleUiException(new OutOfMemoryException());

        Assert.False(handled);
        Assert.Empty(shown);
        Assert.Equal(1, stops);
        Assert.Equal(LogLevel.Fatal, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public void Fatal_StopsVpnOnce_AndLogsEachError()
    {
        var log = new FakeLog();
        var stops = 0;
        var handler = new GlobalExceptionHandler(log, _ => { }, () => { stops++; return Task.CompletedTask; });

        handler.HandleFatal(new InvalidOperationException("first"));
        handler.HandleFatal(null);

        Assert.Equal(1, stops);
        Assert.Equal(2, log.Entries.Count);
        Assert.All(log.Entries, x => Assert.Equal(LogLevel.Fatal, x.Level));
    }

    [Fact]
    public void Fatal_StopThatHangs_IsAbandonedAfterTimeout()
    {
        var log = new FakeLog();
        var never = new TaskCompletionSource();
        var handler = new GlobalExceptionHandler(log, _ => { }, () => never.Task, TimeSpan.FromMilliseconds(50));

        handler.HandleFatal(new InvalidOperationException("boom"));

        Assert.Equal(LogLevel.Warn, log.Entries[^1].Level);
    }

    [Fact]
    public void Fatal_StopFailure_IsLogged()
    {
        var log = new FakeLog();
        var handler = new GlobalExceptionHandler(
            log, _ => { }, () => Task.FromException(new IOException("proxy restore failed")));

        handler.HandleFatal(new InvalidOperationException("boom"));

        Assert.Contains(log.Entries, x => x.Message.Contains("proxy restore failed"));
    }

    [Fact]
    public void UnobservedTask_IsLogged()
    {
        var log = new FakeLog();
        var handler = new GlobalExceptionHandler(log, _ => { }, () => Task.CompletedTask);

        handler.HandleUnobservedTask(new AggregateException(new InvalidOperationException("background")));

        Assert.Contains("background", Assert.Single(log.Entries).Message);
    }

    [Fact]
    public void LoggedException_IsRedactedByLogPort()
    {
        var store = new InMemoryLogStore();
        var handler = new GlobalExceptionHandler(new LogPort(store), _ => { }, () => Task.CompletedTask);
        const string uuid = "123e4567-e89b-12d3-a456-426614174000";

        handler.HandleUiException(new InvalidOperationException($"bad link vless://{uuid}@example.com:443?pbk=secret-key"));

        var text = store.SnapshotAll();
        Assert.Contains("InvalidOperationException", text);
        Assert.DoesNotContain(uuid, text);
        Assert.DoesNotContain("secret-key", text);
    }

    private sealed class FakeLog : ILogPort
    {
        public List<(LogLevel Level, string Source, string Message)> Entries { get; } = [];
        public int Version => Entries.Count;
        public string SnapshotText(LogLevel minLevel) => string.Join("\n", Entries.Select(x => x.Message));
        public string? TryGetLatestMessage(LogLevel minLevel) => Entries.LastOrDefault().Message;
        public void Append(LogLevel level, string source, string message) => Entries.Add((level, source, message));
        public void Clear() => Entries.Clear();
    }
}

using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Diagnostics;

namespace NothingVpn.Infrastructure.Ports;

public sealed class LogPort : ILogPort
{
    private readonly InMemoryLogStore _store;
    private readonly PersistentLogArchive? _archive;

    internal LogPort(InMemoryLogStore store, PersistentLogArchive? archive = null)
    {
        _store = store;
        _archive = archive;
        if (archive is not null) store.Appended += (_, line) => archive.Append(line);
    }

    public bool PersistentLoggingEnabled => _archive?.Enabled ?? false;
    public string? PersistentLogDirectory => _archive?.DirectoryPath;
    public string? PersistentLogError => _archive?.Error;
    public void SetPersistentLogging(bool enabled) => (_archive ?? throw new NotSupportedException()).SetEnabled(enabled);

    public int Version => _store.Version;

    public string SnapshotText(LogLevel minLevel) => _store.SnapshotText(minLevel);

    public string? TryGetLatestMessage(LogLevel minLevel) => _store.TryGetLatestMessage(minLevel);

    public void Append(LogLevel level, string source, string message) =>
        _store.AppendStructured(level, source, message);

    public void AppendAt(LogLevel level, string source, string message, DateTimeOffset timestampUtc) =>
        _store.AppendStructured(level, source, message, timestampUtc: timestampUtc);

    public void Clear() => _store.Clear();
}

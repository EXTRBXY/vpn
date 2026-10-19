using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Diagnostics;

namespace NothingVpn.Infrastructure.Ports;

public sealed class LogPort : ILogPort
{
    private readonly InMemoryLogStore _store;

    internal LogPort(InMemoryLogStore store)
    {
        _store = store;
    }

    public int Version => _store.Version;

    public string SnapshotText(LogLevel minLevel) => _store.SnapshotText(minLevel);

    public string? TryGetLatestMessage(LogLevel minLevel) => _store.TryGetLatestMessage(minLevel);

    public void Append(LogLevel level, string source, string message) =>
        _store.AppendStructured(level, source, message);

    public void Clear() => _store.Clear();
}

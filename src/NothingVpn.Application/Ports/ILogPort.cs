using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Ports;

public interface ILogPort
{
    string SnapshotText(LogLevel minLevel);
    string? TryGetLatestMessage(LogLevel minLevel);
}


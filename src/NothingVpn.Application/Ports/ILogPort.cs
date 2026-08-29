namespace NothingVpn.Application.Ports;

public interface ILogPort
{
    string SnapshotText(int minLevel);
    string? TryGetLatestMessage(int minLevel);
}


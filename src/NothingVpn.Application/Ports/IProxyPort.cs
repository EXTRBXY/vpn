using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface IProxyPort
{
    ProxySettingsSnapshotModel ReadCurrent();
    void Enable(string proxyServer, string proxyOverride);
    void Restore(ProxySettingsSnapshotModel? previous);
}


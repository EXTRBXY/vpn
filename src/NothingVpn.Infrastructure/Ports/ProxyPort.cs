using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.WinInet;

namespace NothingVpn.Infrastructure.Ports;

public sealed class ProxyPort : IProxyPort
{
    private readonly WinInetProxyController _proxy = new();

    public ProxySettingsSnapshotModel ReadCurrent() => _proxy.ReadCurrent();

    public void Enable(string proxyServer, string proxyOverride) => _proxy.Enable(proxyServer, proxyOverride);

    public void Restore(ProxySettingsSnapshotModel? previous)
        => _proxy.Restore(previous);
}


using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface IVpnConnectionService
{
    event EventHandler<bool>? ConnectionStateChanged;
    Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    VpnConnectionStatus GetStatus();
}


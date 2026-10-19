using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;

namespace NothingVpn.Presentation;

public sealed class ConnectionController : IConnectionController
{
    private readonly IVpnConnectionService _vpnConnectionService;
    private readonly IElevationPort _elevationPort;

    public ConnectionController(
        IVpnConnectionService vpnConnectionService,
        IElevationPort elevationPort)
    {
        _vpnConnectionService = vpnConnectionService;
        _elevationPort = elevationPort;
    }

    public event EventHandler<bool>? ConnectionStateChanged
    {
        add => _vpnConnectionService.ConnectionStateChanged += value;
        remove => _vpnConnectionService.ConnectionStateChanged -= value;
    }

    public bool IsRunning => _vpnConnectionService.GetStatus().IsRunning;

    public bool IsAdministrator => _elevationPort.IsAdministrator();

    public VpnConnectionStatus GetStatus() => _vpnConnectionService.GetStatus();

    public async Task<ConnectionStartOutcome> StartAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _vpnConnectionService.ConnectAsync(
                new ConnectRequest { ProfileId = profileId },
                cancellationToken).ConfigureAwait(false);
            return new ConnectionStartOutcome(result.Started);
        }
        catch
        {
            try { await _vpnConnectionService.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { }
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _vpnConnectionService.DisconnectAsync(cancellationToken);
}

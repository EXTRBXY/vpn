using NothingVpn.Application.Models;
using NothingVpn.Application.Services;

namespace NothingVpn.Presentation;

public sealed class ConnectionController : IConnectionController
{
    private readonly IVpnConnectionService _vpnConnectionService;
    private readonly IAppLifecycleService _appLifecycleService;

    public ConnectionController(
        IVpnConnectionService vpnConnectionService,
        IAppLifecycleService appLifecycleService)
    {
        _vpnConnectionService = vpnConnectionService;
        _appLifecycleService = appLifecycleService;
    }

    public event EventHandler<bool>? ConnectionStateChanged
    {
        add => _vpnConnectionService.ConnectionStateChanged += value;
        remove => _vpnConnectionService.ConnectionStateChanged -= value;
    }

    public bool IsRunning => _vpnConnectionService.GetStatus().IsRunning;

    public bool IsAdministrator => _appLifecycleService.IsAdministrator();

    public VpnConnectionStatus GetStatus() => _vpnConnectionService.GetStatus();

    public async Task<ConnectionStartOutcome> StartAsync(
        string profileId,
        string mode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _vpnConnectionService.ConnectAsync(
                new ConnectRequest { ProfileId = profileId },
                cancellationToken).ConfigureAwait(false);

            if (!result.RequiresElevation)
                return new ConnectionStartOutcome(result.Started, ExitCurrentProcess: false);

            var arguments = result.ElevationArgs
                ?? _appLifecycleService.BuildTakeoverArgs(mode, profileId);
            if (!_appLifecycleService.RestartElevated(arguments))
                throw new InvalidOperationException("TUN требует прав администратора (запрос UAC был отменён).");

            return new ConnectionStartOutcome(Connected: false, ExitCurrentProcess: true);
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

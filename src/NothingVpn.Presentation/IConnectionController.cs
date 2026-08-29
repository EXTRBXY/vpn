using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public interface IConnectionController
{
    event EventHandler<bool>? ConnectionStateChanged;
    bool IsRunning { get; }
    bool IsAdministrator { get; }
    VpnConnectionStatus GetStatus();
    Task<ConnectionStartOutcome> StartAsync(
        string profileId,
        string mode,
        CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

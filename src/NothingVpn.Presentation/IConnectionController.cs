using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Presentation;

public interface IConnectionController
{
    event EventHandler<bool>? ConnectionStateChanged;
    bool IsRunning { get; }
    bool IsAdministrator { get; }
    VpnConnectionStatus GetStatus();
    Task<ConnectionStartOutcome> StartAsync(
        string profileId,
        ConnectionMode mode,
        CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

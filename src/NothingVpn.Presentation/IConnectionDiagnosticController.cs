using NothingVpn.Domain.Models;

namespace NothingVpn.Presentation;

public interface IConnectionDiagnosticController
{
    Task<ConnectionDiagnosticResult> RunAsync(
        ConnectionMode connectionMode,
        bool isRunning,
        CancellationToken cancellationToken = default);
}

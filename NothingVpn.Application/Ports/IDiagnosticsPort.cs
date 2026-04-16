namespace NothingVpn.Application.Ports;

public interface IDiagnosticsPort
{
    Task<(bool Success, string? Error)> ProxySmokeTestAsync(string proxyHost, int proxyPort, string targetHost, int targetPort, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<(bool Success, string? Error)> TunSmokeTestAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}


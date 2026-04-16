using NothingVpn.Application.Ports;
using NothingVpn.Tray.Internal.Diagnostics;

namespace NothingVpn.Infrastructure.Ports;

public sealed class DiagnosticsPort : IDiagnosticsPort
{
    public async Task<(bool Success, string? Error)> ProxySmokeTestAsync(
        string proxyHost,
        int proxyPort,
        string targetHost,
        int targetPort,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var result = await ProxySmokeTest.HttpConnectAsync(proxyHost, proxyPort, targetHost, targetPort, timeout);
        return (result.Success, result.Error);
    }

    public async Task<(bool Success, string? Error)> TunSmokeTestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var result = await TunSmokeTest.IpifyAsync(timeout);
        return (result.Success, result.Error);
    }
}


using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Diagnostics;

namespace NothingVpn.Infrastructure.Ports;

public sealed class DiagnosticsPort : IDiagnosticsPort
{
    public async Task<(bool Success, string? Error)> CanReachTcpAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var result = await ProxySmokeTest.TcpConnectAsync(host, port, timeout, cancellationToken);
        return (result.Success, result.Error);
    }

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


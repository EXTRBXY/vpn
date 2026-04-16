using NothingVpn.Application.Ports;

namespace NothingVpn.Application.Services;

public sealed class DiagnosticsService(IDiagnosticsPort diagnosticsPort, ILogPort logPort, IStateStorePort stateStore) : IDiagnosticsService
{
    public async Task<(bool Success, string? Error)> RunProxySmokeTestAsync(string targetHost, int targetPort, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var state = stateStore.Load();
        return await diagnosticsPort.ProxySmokeTestAsync(
            proxyHost: "127.0.0.1",
            proxyPort: state.LocalMixedPort,
            targetHost: targetHost,
            targetPort: targetPort,
            timeout: timeout,
            cancellationToken: cancellationToken);
    }

    public Task<(bool Success, string? Error)> RunTunSmokeTestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => diagnosticsPort.TunSmokeTestAsync(timeout, cancellationToken);

    public string GetLogsText(int minLevel) => logPort.SnapshotText(minLevel);

    public void ExportLogs(string path, int minLevel)
    {
        var text = logPort.SnapshotText(minLevel);
        File.WriteAllText(path, text);
    }
}


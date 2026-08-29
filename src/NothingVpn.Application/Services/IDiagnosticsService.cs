namespace NothingVpn.Application.Services;

public interface IDiagnosticsService
{
    Task<(bool Success, string? Error)> RunProxySmokeTestAsync(string targetHost, int targetPort, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<(bool Success, string? Error)> RunTunSmokeTestAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    string GetLogsText(int minLevel);
    void ExportLogs(string path, int minLevel);
}


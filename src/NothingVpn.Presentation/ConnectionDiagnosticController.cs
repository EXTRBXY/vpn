using System.Diagnostics;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Presentation;

public sealed class ConnectionDiagnosticController : IConnectionDiagnosticController
{
    private readonly IDiagnosticsService _diagnosticsService;

    public ConnectionDiagnosticController(IDiagnosticsService diagnosticsService)
    {
        _diagnosticsService = diagnosticsService;
    }

    public async Task<ConnectionDiagnosticResult> RunAsync(
        string? connectionMode,
        bool isRunning,
        CancellationToken cancellationToken = default)
    {
        if (!isRunning)
        {
            return new ConnectionDiagnosticResult(
                ConnectionDiagnosticStatus.NotRunning,
                "Сначала нажмите «Старт».");
        }

        var mode = ConnectionPolicy.NormalizeMode(connectionMode);
        var isTun = ConnectionPolicy.IsTunMode(mode);
        var isTunApps = string.Equals(mode, ConnectionPolicy.TunAppsMode, StringComparison.OrdinalIgnoreCase);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = isTun
                ? await _diagnosticsService.RunTunSmokeTestAsync(TimeSpan.FromSeconds(4), cancellationToken)
                : await _diagnosticsService.RunProxySmokeTestAsync(
                    "api.ipify.org",
                    443,
                    TimeSpan.FromSeconds(8),
                    cancellationToken);
            stopwatch.Stop();

            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? (isTun ? "TUN test failed." : "Proxy test failed."));

            var elapsed = stopwatch.ElapsedMilliseconds;
            var message = isTunApps
                ? $"Связность: OK\nВремя: {elapsed} мс\n\nВажно: в режиме «TUN (выбранные приложения)» тест идёт из процесса Nothing VPN (обычно напрямую, без VLESS). OK здесь не означает, что выбранные .exe ходят в интернет через туннель — проверяйте сами браузер/игру из списка."
                : $"{(isTun ? "TUN" : "Прокси")}: OK\nВремя: {elapsed} мс";
            return new ConnectionDiagnosticResult(ConnectionDiagnosticStatus.Success, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var label = isTun ? "TUN" : "Прокси";
            var logMessage = $"{label} smoke test: FAIL, {stopwatch.ElapsedMilliseconds} ms, reason: {ex.Message}";
            var message = $"{label}: FAIL\n{ex.Message}\nВремя: {stopwatch.ElapsedMilliseconds} мс";
            return new ConnectionDiagnosticResult(ConnectionDiagnosticStatus.Failure, message, logMessage);
        }
    }
}

using NothingVpn.Application.Services;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class ConnectionDiagnosticControllerTests
{
    [Fact]
    public async Task RunAsync_NotRunning_DoesNotCallDiagnostics()
    {
        var diagnostics = new FakeDiagnosticsService();
        var controller = new ConnectionDiagnosticController(diagnostics);

        var result = await controller.RunAsync("proxy", isRunning: false);

        Assert.Equal(ConnectionDiagnosticStatus.NotRunning, result.Status);
        Assert.Equal(0, diagnostics.ProxyCalls + diagnostics.TunCalls);
    }

    [Fact]
    public async Task RunAsync_ProxyMode_UsesProxySmokeTest()
    {
        var diagnostics = new FakeDiagnosticsService();
        var controller = new ConnectionDiagnosticController(diagnostics);

        var result = await controller.RunAsync("proxy", isRunning: true);

        Assert.Equal(ConnectionDiagnosticStatus.Success, result.Status);
        Assert.StartsWith("Прокси: OK", result.Message);
        Assert.Equal(1, diagnostics.ProxyCalls);
        Assert.Equal(0, diagnostics.TunCalls);
        Assert.Equal("api.ipify.org", diagnostics.TargetHost);
        Assert.Equal(443, diagnostics.TargetPort);
    }

    [Fact]
    public async Task RunAsync_TunAppsMode_ReturnsScopeWarning()
    {
        var diagnostics = new FakeDiagnosticsService();
        var controller = new ConnectionDiagnosticController(diagnostics);

        var result = await controller.RunAsync("tun_apps", isRunning: true);

        Assert.Equal(ConnectionDiagnosticStatus.Success, result.Status);
        Assert.Contains("выбранные приложения", result.Message);
        Assert.Contains("не означает", result.Message);
        Assert.Equal(1, diagnostics.TunCalls);
    }

    [Fact]
    public async Task RunAsync_FailedTest_ReturnsFailureAndLogMessage()
    {
        var diagnostics = new FakeDiagnosticsService
        {
            ProxyResult = (false, "Connection refused.")
        };
        var controller = new ConnectionDiagnosticController(diagnostics);

        var result = await controller.RunAsync("proxy", isRunning: true);

        Assert.Equal(ConnectionDiagnosticStatus.Failure, result.Status);
        Assert.Contains("Connection refused.", result.Message);
        Assert.Contains("smoke test: FAIL", result.LogMessage);
    }

    private sealed class FakeDiagnosticsService : IDiagnosticsService
    {
        public (bool Success, string? Error) ProxyResult { get; init; } = (true, null);
        public (bool Success, string? Error) TunResult { get; init; } = (true, null);
        public int ProxyCalls { get; private set; }
        public int TunCalls { get; private set; }
        public string? TargetHost { get; private set; }
        public int TargetPort { get; private set; }

        public Task<(bool Success, string? Error)> RunProxySmokeTestAsync(
            string targetHost,
            int targetPort,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ProxyCalls++;
            TargetHost = targetHost;
            TargetPort = targetPort;
            return Task.FromResult(ProxyResult);
        }

        public Task<(bool Success, string? Error)> RunTunSmokeTestAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            TunCalls++;
            return Task.FromResult(TunResult);
        }

        public string GetLogsText(int minLevel) => throw new NotSupportedException();
        public void ExportLogs(string path, int minLevel) => throw new NotSupportedException();
    }
}

namespace NothingVpn.Presentation;

public interface IConnectionDiagnosticController
{
    Task<ConnectionDiagnosticResult> RunAsync(
        string? connectionMode,
        bool isRunning,
        CancellationToken cancellationToken = default);
}

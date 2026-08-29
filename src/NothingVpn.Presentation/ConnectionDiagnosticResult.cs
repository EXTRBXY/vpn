namespace NothingVpn.Presentation;

public sealed record ConnectionDiagnosticResult(
    ConnectionDiagnosticStatus Status,
    string Message,
    string? LogMessage = null);

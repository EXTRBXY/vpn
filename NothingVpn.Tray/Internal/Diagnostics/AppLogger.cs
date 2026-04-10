using NothingVpn.Tray.Internal.Security;

namespace NothingVpn.Tray.Internal.Diagnostics;

internal sealed class AppLogger
{
    private readonly InMemoryLogStore _store;

    public AppLogger(InMemoryLogStore store)
    {
        _store = store;
    }

    public void Trace(string source, string message) => Write(0, source, message);
    public void Debug(string source, string message) => Write(1, source, message);
    public void Info(string source, string message) => Write(2, source, message);
    public void Warn(string source, string message) => Write(3, source, message);
    public void Error(string source, string message) => Write(4, source, message);

    public void Error(string source, Exception ex, string message)
    {
        var details = $"{message} | {ex.GetType().Name}: {ex.Message}";
        Write(4, source, details);
    }

    private void Write(int level, string source, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var safeMessage = LogRedactor.Redact(message);
        var safeSource = string.IsNullOrWhiteSpace(source) ? "app" : source.Trim();
        _store.AppendStructured(
            level: level,
            source: safeSource,
            message: safeMessage,
            raw: message,
            timestampUtc: DateTimeOffset.UtcNow);
    }
}

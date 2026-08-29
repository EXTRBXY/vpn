using System.Text;
using NothingVpn.Infrastructure.Security;

namespace NothingVpn.Infrastructure.Diagnostics;

public sealed class InMemoryLogStore
{
    private readonly object _gate = new();
    private readonly Queue<LogEntry> _entries = new();
    private int _bytes;
    private int _version;

    public InMemoryLogStore(int maxBytes = 1_000_000)
    {
        if (maxBytes < 16_000) maxBytes = 16_000;
        MaxBytes = maxBytes;
    }

    public int MaxBytes { get; }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _bytes = 0;
            _version++;
        }
    }

    public void AppendStructured(
        int level,
        string source,
        string message,
        string? raw = null,
        DateTimeOffset? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(raw))
            return;

        var safeMessage = LogRedactor.Redact(string.IsNullOrWhiteSpace(message) ? raw! : message);
        if (string.IsNullOrWhiteSpace(safeMessage))
            return;

        var safeRaw = LogRedactor.Redact(string.IsNullOrWhiteSpace(raw) ? safeMessage : raw!);
        var ts = timestampUtc ?? DateTimeOffset.UtcNow;
        var src = string.IsNullOrWhiteSpace(source) ? "app" : source.Trim();
        var line = $"[{ts:O}] [{LevelToText(level)}] [{src}] {safeMessage}";
        var entryBytes = Encoding.UTF8.GetByteCount(line) + 1;

        lock (_gate)
        {
            _entries.Enqueue(new LogEntry(ts, level, src, safeMessage, safeRaw, line, entryBytes));
            _bytes += entryBytes;
            _version++;

            while (_bytes > MaxBytes && _entries.Count > 0)
            {
                var removed = _entries.Dequeue();
                _bytes -= removed.ByteSize;
            }
        }
    }

    public bool TryGetVersion(out int version)
    {
        lock (_gate)
        {
            version = _version;
            return _entries.Count != 0;
        }
    }

    public string SnapshotText(int minLevel) => SnapshotText(minLevel, out _);

    public string SnapshotText(int minLevel, out int version)
    {
        LogEntry[] snapshot;
        int bytes;
        lock (_gate)
        {
            version = _version;
            if (_entries.Count == 0) return "";
            bytes = _bytes;
            snapshot = _entries.ToArray();
        }

        var sb = new StringBuilder(Math.Min(bytes, 200_000));
        foreach (var e in snapshot)
        {
            if (e.Level < minLevel) continue;
            sb.AppendLine(e.Line);
        }
        return sb.ToString();
    }

    public string SnapshotAll() => SnapshotText(minLevel: 0);

    public string? TryGetLatestMessage(int minLevel)
    {
        lock (_gate)
        {
            if (_entries.Count == 0) return null;
            foreach (var e in _entries.Reverse())
            {
                if (e.Level >= minLevel)
                    return e.Message;
            }
        }

        return null;
    }

    private static string LevelToText(int level)
    {
        return level switch
        {
            <= 0 => "TRACE",
            1 => "DEBUG",
            2 => "INFO",
            3 => "WARN",
            4 => "ERROR",
            5 => "FATAL",
            _ => "PANIC"
        };
    }

    private readonly record struct LogEntry(
        DateTimeOffset TimestampUtc,
        int Level,
        string Source,
        string Message,
        string Raw,
        string Line,
        int ByteSize);
}

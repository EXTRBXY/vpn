using System.Text;
using NothingVpn.Domain.Models;
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

    /// <summary>Запись добавлена (уже замаскированная). Служба пересылает её подписчикам канала.</summary>
    public event EventHandler<LogLine>? Appended;

    /// <summary>Буфер очищен (sing-box очищает его при каждом старте).</summary>
    public event EventHandler? Cleared;

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _bytes = 0;
            _version++;
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public void AppendStructured(
        LogLevel level,
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

        Appended?.Invoke(this, new LogLine(ts, level, src, safeMessage));
    }

    public int Version
    {
        get
        {
            lock (_gate)
                return _version;
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

    public string SnapshotText(LogLevel minLevel) => SnapshotText(minLevel, out _);

    public string SnapshotText(LogLevel minLevel, out int version)
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

    public string SnapshotAll() => SnapshotText(LogLevel.Trace);

    public IReadOnlyList<LogLine> SnapshotLines()
    {
        lock (_gate)
            return _entries.Select(e => new LogLine(e.TimestampUtc, e.Level, e.Source, e.Message)).ToList();
    }

    public string? TryGetLatestMessage(LogLevel minLevel)
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

    private static string LevelToText(LogLevel level)
    {
        return level switch
        {
            <= LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warn => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Fatal => "FATAL",
            _ => "PANIC"
        };
    }

    private readonly record struct LogEntry(
        DateTimeOffset TimestampUtc,
        LogLevel Level,
        string Source,
        string Message,
        string Raw,
        string Line,
        int ByteSize);
}

public readonly record struct LogLine(DateTimeOffset TimestampUtc, LogLevel Level, string Source, string Message);

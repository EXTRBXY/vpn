using System.Text;

namespace NothingVpn.Tray.Internal.Diagnostics;

internal sealed class InMemoryLogStore
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

    public void Append(int level, string line)
    {
        if (line is null) return;
        // UTF-8 byte size approximation; exact enough for our cap.
        var entryBytes = Encoding.UTF8.GetByteCount(line) + 1;

        lock (_gate)
        {
            _entries.Enqueue(new LogEntry(level, line));
            _bytes += entryBytes;
            _version++;

            while (_bytes > MaxBytes && _entries.Count > 0)
            {
                var removed = _entries.Dequeue();
                _bytes -= Encoding.UTF8.GetByteCount(removed.Line) + 1;
            }
        }
    }

    public string SnapshotText(int minLevel)
    {
        return SnapshotText(minLevel, out _);
    }

    public string SnapshotText(int minLevel, out int version)
    {
        lock (_gate)
        {
            version = _version;
            if (_entries.Count == 0) return "";
            var sb = new StringBuilder(Math.Min(_bytes, 200_000));
            foreach (var e in _entries)
            {
                if (e.Level < minLevel) continue;
                sb.AppendLine(e.Line);
            }
            return sb.ToString();
        }
    }

    public string SnapshotAll()
    {
        return SnapshotText(minLevel: 0);
    }

    private readonly record struct LogEntry(int Level, string Line);
}


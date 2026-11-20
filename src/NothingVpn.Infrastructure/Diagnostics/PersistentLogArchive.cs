using System.Text;
using NothingVpn.Infrastructure.Security;

namespace NothingVpn.Infrastructure.Diagnostics;

/// <summary>
/// Отдельно включаемый архив журнала интерфейса. На диск пишет один фоновый поток,
/// чтобы канал службы и UI не ждали файл.
/// </summary>
internal sealed class PersistentLogArchive : IDisposable
{
    private const int MaxFileBytes = 1_000_000;
    private const int MaxFiles = 5;
    private const int MaxSourceChars = 1_000;
    private const int MaxMessageChars = 100_000;
    private const int MaxPending = 64;
    private static readonly TimeSpan SpaceWait = TimeSpan.FromSeconds(2);
    private static readonly byte[] Utf8Bom = Encoding.UTF8.GetPreamble();

    private readonly object _gate = new();
    private readonly Queue<LogLine> _pending = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _idle = new(true);
    private readonly Thread _writer;
    private readonly string _marker;
    private int _accepted;
    private int _completed;
    private int _disposeOnce;
    private bool _enabled;
    private bool _stop;
    private string? _error;

    public PersistentLogArchive(string directoryPath)
    {
        DirectoryPath = directoryPath;
        _marker = Path.Combine(directoryPath, "persistent-logging.enabled");
        _enabled = File.Exists(_marker);
        _writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "NothingVpn.LogArchive"
        };
        _writer.Start();
    }

    public string DirectoryPath { get; }
    public bool Enabled { get { lock (_gate) return _enabled; } }
    public string? Error { get { lock (_gate) return _error; } }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (enabled)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(_marker, "enabled");
            }
            else if (File.Exists(_marker))
                File.Delete(_marker);
            _enabled = enabled;
            _error = null;
            Monitor.PulseAll(_gate);
        }
    }

    public void Append(LogLine entry)
    {
        lock (_gate)
        {
            if (!_enabled || _stop)
                return;
            if (_pending.Count >= MaxPending)
                _error = "Запись архива не успевает.";
            if (!WaitForSpace())
            {
                if (_enabled && !_stop)
                    _error = "Запись архива не успевает. Строка пропущена.";
                return;
            }

            if (!_enabled || _stop)
                return;

            _pending.Enqueue(entry);
            _accepted++;
            _idle.Reset();
            _signal.Set();
        }
    }

    public void Flush()
    {
        if (!_idle.Wait(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("Архив журнала не записал строки за отведённое время.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeOnce, 1) != 0)
            return;
        lock (_gate)
        {
            _stop = true;
            Monitor.PulseAll(_gate);
        }

        _signal.Set();
        if (!_writer.Join(TimeSpan.FromSeconds(5)))
            return;
        _signal.Dispose();
        _idle.Dispose();
    }

    private bool WaitForSpace()
    {
        var deadline = Environment.TickCount64 + (long)SpaceWait.TotalMilliseconds;
        while (_pending.Count >= MaxPending && _enabled && !_stop)
        {
            var left = deadline - Environment.TickCount64;
            if (left <= 0)
                return false;
            // Wait отпускает _gate: писатель или SetEnabled могут разбудить вызывающего.
            Monitor.Wait(_gate, TimeSpan.FromMilliseconds(left));
        }

        return _pending.Count < MaxPending && _enabled && !_stop;
    }

    private void WriteLoop()
    {
        while (true)
        {
            _signal.WaitOne();
            while (Dequeue(out var entry))
            {
                try
                {
                    Write(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lock (_gate)
                        _error = ex.Message;
                }

                SignalWritten();
            }

            lock (_gate)
            {
                if (_pending.Count == 0 && _completed == _accepted)
                    _idle.Set();
                if (_stop && _pending.Count == 0)
                    return;
            }
        }
    }

    private bool Dequeue(out LogLine entry)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                entry = default;
                return false;
            }

            entry = _pending.Dequeue();
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    private void SignalWritten()
    {
        lock (_gate)
        {
            _completed++;
            if (_pending.Count == 0 && _completed == _accepted)
                _idle.Set();
        }
    }

    private void Write(LogLine entry)
    {
        var bytes = Encode(entry);
        var current = LogPath(0);
        if (File.Exists(current) && new FileInfo(current).Length + bytes.Length > MaxFileBytes)
            Rotate();

        var path = LogPath(0);
        var addBom = !File.Exists(path) || new FileInfo(path).Length == 0;
        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        if (addBom)
            file.Write(Utf8Bom);
        file.Write(bytes);
        lock (_gate)
        {
            if (_pending.Count < MaxPending)
                _error = null;
        }
    }

    private static byte[] Encode(LogLine entry)
    {
        var source = OneLine(LogRedactor.Redact(entry.Source), MaxSourceChars);
        var message = OneLine(LogRedactor.Redact(entry.Message), MaxMessageChars);
        var line = $"[{entry.TimestampUtc:O}] [{InMemoryLogStore.LevelToText(entry.Level)}] [{source}] {message}{Environment.NewLine}";
        return Encoding.UTF8.GetBytes(line);
    }

    private static string OneLine(string text, int maxChars)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        if (text.Length <= maxChars)
            return text;
        var cut = maxChars;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
            cut--;
        return string.Concat(text.AsSpan(0, cut), " [обрезано]");
    }

    private void Rotate()
    {
        for (var i = MaxFiles - 1; i > 0; i--)
        {
            var previous = LogPath(i - 1);
            if (File.Exists(previous))
                File.Move(previous, LogPath(i), overwrite: true);
        }
    }

    private string LogPath(int index) =>
        Path.Combine(DirectoryPath, index == 0 ? "developer.log" : $"developer.{index}.log");
}

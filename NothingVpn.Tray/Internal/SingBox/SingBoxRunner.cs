using System.Diagnostics;
using System.Text.RegularExpressions;
using NothingVpn.Tray.Internal.Diagnostics;
using NothingVpn.Tray.Internal.Store;
using NothingVpn.Tray.Internal.Security;

namespace NothingVpn.Tray.Internal.SingBox;

internal sealed class SingBoxRunner : IDisposable
{
    private readonly AppPaths _paths;
    private readonly string _singBoxExePathHint;
    private readonly Func<bool> _debugLogs;
    private readonly Func<string?> _trustedSha256;
    private readonly InMemoryLogStore _logStore;
    private Process? _process;
    private readonly object _gate = new();

    public event EventHandler? ProcessExited;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public SingBoxRunner(
        AppPaths paths,
        string singBoxExePath,
        InMemoryLogStore logStore,
        Func<bool>? debugLogs = null,
        Func<string?>? trustedSha256 = null)
    {
        _paths = paths;
        _singBoxExePathHint = singBoxExePath;
        _logStore = logStore;
        _debugLogs = debugLogs ?? (() => false);
        _trustedSha256 = trustedSha256 ?? (() => null);
    }

    public void Start(string configPath)
    {
        lock (_gate)
        {
            if (_process is { HasExited: false })
                throw new InvalidOperationException("sing-box is already running.");

            var exePath = ResolveSingBoxExePath(_singBoxExePathHint);
            if (exePath is null)
                throw new FileNotFoundException(
                    "sing-box.exe not found. Put it next to the app or into a ./bin folder near it.",
                    _singBoxExePathHint);

            var trusted = _trustedSha256();
            if (!string.IsNullOrWhiteSpace(trusted))
            {
                var actual = FileHash.Sha256Hex(exePath);
                if (!string.Equals(actual, trusted, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"sing-box.exe hash mismatch.\nTrusted: {trusted}\nActual:   {actual}");
            }

            // Logs are kept in-memory to minimize disk IO and avoid a logs folder.
            // Users can export logs on demand from the UI.
            _logStore.Clear();

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"run -c \"{configPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(exePath)!
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Exited += (_, _) => ProcessExited?.Invoke(this, EventArgs.Empty);

            if (!p.Start())
                throw new InvalidOperationException("Failed to start sing-box process.");

            _process = p;

            // Async log pumping
            _ = Task.Run(() => PumpAsync(p.StandardOutput, redact: !_debugLogs(), _logStore));
            _ = Task.Run(() => PumpAsync(p.StandardError, redact: !_debugLogs(), _logStore));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_process is null) return;
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(3000);
                }
            }
            catch
            {
                // best-effort
            }
            finally
            {
                try { _process.Dispose(); } catch { }
                _process = null;
            }
        }
    }

    private static async Task PumpAsync(StreamReader reader, bool redact, InMemoryLogStore store)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line is null) break;
                var clean = StripAnsi(line);
                var outLine = redact ? LogRedactor.Redact(clean) : clean;
                var (lvl, lvlText) = DetectLevel(outLine);
                store.Append(lvl, $"[{DateTimeOffset.Now:O}] [{lvlText}] {outLine}");
            }
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    private static string StripAnsi(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return AnsiRegex.Replace(s, "");
    }

    private static readonly Regex SingBoxLevelPrefix = new(@"^(TRAC|DEBU|INFO|WARN|ERRO|FATA|PANI)\[\d+\]", RegexOptions.Compiled);
    private static readonly Regex BracketLevelPrefix = new(@"^\[(TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|PANIC)\]\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WordLevelPrefix = new(@"^(TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|PANIC)\b[:\s\-]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LevelKeyValue = new(@"\blevel=(trace|debug|info|warn|warning|error|fatal|panic)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static (int Level, string Text) DetectLevel(string s)
    {
        // sing-box format: INFO[0001] ..., WARN[0001] ..., ERRO[0001] ..., DEBU[0001] ...
        if (string.IsNullOrEmpty(s)) return (2, "INFO");

        var t = s.TrimStart();

        var m = SingBoxLevelPrefix.Match(t);
        if (m.Success)
        {
            return m.Groups[1].Value switch
            {
                "TRAC" => (0, "TRACE"),
                "DEBU" => (1, "DEBUG"),
                "INFO" => (2, "INFO"),
                "WARN" => (3, "WARN"),
                "ERRO" => (4, "ERROR"),
                "FATA" => (5, "FATAL"),
                "PANI" => (6, "PANIC"),
                _ => (2, "INFO")
            };
        }

        var m2 = BracketLevelPrefix.Match(t);
        if (m2.Success)
        {
            var v = m2.Groups[1].Value.ToUpperInvariant();
            if (v == "WARNING") v = "WARN";
            return v switch
            {
                "TRACE" => (0, "TRACE"),
                "DEBUG" => (1, "DEBUG"),
                "INFO" => (2, "INFO"),
                "WARN" => (3, "WARN"),
                "ERROR" => (4, "ERROR"),
                "FATAL" => (5, "FATAL"),
                "PANIC" => (6, "PANIC"),
                _ => (2, "INFO")
            };
        }

        var m3 = WordLevelPrefix.Match(t);
        if (m3.Success)
        {
            var v = m3.Groups[1].Value.ToUpperInvariant();
            if (v == "WARNING") v = "WARN";
            return v switch
            {
                "TRACE" => (0, "TRACE"),
                "DEBUG" => (1, "DEBUG"),
                "INFO" => (2, "INFO"),
                "WARN" => (3, "WARN"),
                "ERROR" => (4, "ERROR"),
                "FATAL" => (5, "FATAL"),
                "PANIC" => (6, "PANIC"),
                _ => (2, "INFO")
            };
        }

        var m4 = LevelKeyValue.Match(t);
        if (m4.Success)
        {
            var v = m4.Groups[1].Value.ToLowerInvariant();
            return v switch
            {
                "trace" => (0, "TRACE"),
                "debug" => (1, "DEBUG"),
                "info" => (2, "INFO"),
                "warn" => (3, "WARN"),
                "warning" => (3, "WARN"),
                "error" => (4, "ERROR"),
                "fatal" => (5, "FATAL"),
                "panic" => (6, "PANIC"),
                _ => (2, "INFO")
            };
        }

        // If format changes, default to INFO rather than producing false errors.
        return (2, "INFO");
    }

    public void Dispose()
    {
        Stop();
    }

    private static string? ResolveSingBoxExePath(string hint)
    {
        // If hint is absolute or directly exists.
        if (!string.IsNullOrWhiteSpace(hint) && File.Exists(hint))
            return Path.GetFullPath(hint);

        // Search relative to the actual runtime folder.
        // AppContext.BaseDirectory is typically ...\bin\Release\net8.0-windows\
        var baseDir = AppContext.BaseDirectory;

        var candidates = new List<string>
        {
            Path.Combine(baseDir, hint),
            Path.Combine(baseDir, "sing-box.exe"),
            Path.Combine(baseDir, "bin", "sing-box.exe"),
        };

        // Walk up a few levels (net8.0-windows -> Release -> bin -> project)
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 5 && dir.Parent is not null; i++)
        {
            dir = dir.Parent;
            candidates.Add(Path.Combine(dir.FullName, "sing-box.exe"));
            candidates.Add(Path.Combine(dir.FullName, "bin", "sing-box.exe"));
        }

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(c))
                return c;
        }

        return null;
    }
}


using System.Diagnostics;
using System.Text.RegularExpressions;
using NothingVpn.Tray.Internal.Diagnostics;
using NothingVpn.Tray.Internal.Security;
using NothingVpn.Tray.Internal.Store;
using NothingVpn.Tray.Internal.Windows;

namespace NothingVpn.Tray.Internal.SingBox;

internal sealed class SingBoxRunner : IDisposable
{
    private readonly AppPaths _paths;
    private readonly string _singBoxExePathHint;
    private readonly Func<bool> _debugLogs;
    private readonly Func<string?> _trustedSha256;
    private readonly InMemoryLogStore _logStore;
    private Process? _process;
    private ProcessJobScope? _processJob;
    private string? _lastConfigPath;
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
            var exePath = ResolveSingBoxExePath(_singBoxExePathHint);
            if (exePath is null)
                throw new FileNotFoundException(
                    "sing-box.exe not found. Put it next to the app or into a ./bin folder near it.",
                    _singBoxExePathHint);

            if (_process is { HasExited: false })
            {
                if (TunInterfaceCleaner.TryReadInterfaceName(configPath) is not null)
                    StopLocked();
                else
                    throw new InvalidOperationException("sing-box is already running.");
            }

            PrepareTunResources(exePath, configPath);

            var trusted = _trustedSha256();
            if (!string.IsNullOrWhiteSpace(trusted))
            {
                var actual = FileHash.Sha256Hex(exePath);
                if (!string.Equals(actual, trusted, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"sing-box.exe hash mismatch.\nTrusted: {trusted}\nActual:   {actual}");
            }

            var checkError = TryCheckConfig(exePath, configPath);
            if (checkError is not null)
                throw new InvalidOperationException(checkError);

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
            p.Exited += (_, _) =>
            {
                string? tunConfig;
                lock (_gate)
                {
                    _processJob?.Dispose();
                    _processJob = null;
                    _process = null;
                    tunConfig = _lastConfigPath;
                }

                ReleaseTunIfNeeded(tunConfig);
                ProcessExited?.Invoke(this, EventArgs.Empty);
            };

            if (!p.Start())
                throw new InvalidOperationException("Failed to start sing-box process.");

            _processJob?.Dispose();
            _processJob = ProcessJobScope.TryAttach(p);
            _process = p;
            _lastConfigPath = configPath;

            // Async log pumping
            _ = Task.Run(() => PumpAsync(p.StandardOutput, "sing-box/stdout", redact: !_debugLogs(), _logStore));
            _ = Task.Run(() => PumpAsync(p.StandardError, "sing-box/stderr", redact: !_debugLogs(), _logStore));
        }
    }

    private void PrepareTunResources(string exePath, string configPath)
    {
        if (TunInterfaceCleaner.TryReadInterfaceName(configPath) is null)
            return;

        VpnRuntimeRecovery.PrepareTunConnect(exePath, configPath);
    }

    private void StopLocked()
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
            _processJob?.Dispose();
            _processJob = null;
            try { _process.Dispose(); } catch { }
            _process = null;
            ReleaseTunIfNeeded(_lastConfigPath);
        }
    }

    private static void ReleaseTunIfNeeded(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return;

        var name = TunInterfaceCleaner.TryReadInterfaceName(configPath);
        if (!string.IsNullOrWhiteSpace(name))
            VpnRuntimeRecovery.ReleaseAdapterByName(name);
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    private static async Task PumpAsync(StreamReader reader, string source, bool redact, InMemoryLogStore store)
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
                (lvl, lvlText, outLine) = NormalizeSeverity(lvl, lvlText, outLine);
                outLine = StripLevelPrefix(outLine);
                store.AppendStructured(
                    level: lvl,
                    source: source,
                    message: outLine,
                    raw: line,
                    timestampUtc: DateTimeOffset.UtcNow);
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

    private static readonly Regex SingBoxLevelPrefix = new(@"^(TRAC|DEBU|INFO|WARN|ERRO|ERROR|FATA|PANI)\[\d+\]\s*", RegexOptions.Compiled);
    private static readonly Regex SingBoxErrorPrefix = new(@"^(ERRO|ERROR)(?=\[\d+\])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BracketLevelPrefix = new(@"^\[(TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|PANIC)\]\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WordLevelPrefix = new(@"^(TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|PANIC)\b[:\s\-]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LevelKeyValue = new(@"\blevel=(trace|debug|info|warn|warning|error|fatal|panic)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static (int Level, string Text) DetectLevel(string s)
    {
        // sing-box format: INFO[0001] ..., WARN[0001] ..., ERRO[0001] ..., DEBU[0001] ...
        if (string.IsNullOrEmpty(s)) return (2, "INFO");

        var t = s.TrimStart();

        if (TryParseFastLevel(t, out var fastLevel, out var fastText))
            return (fastLevel, fastText);

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
                "ERROR" => (4, "ERROR"),
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

    private static bool TryParseFastLevel(string s, out int level, out string text)
    {
        level = 2;
        text = "INFO";
        if (s.Length >= 5 && s[4] == '[')
        {
            if (s.StartsWith("TRAC", StringComparison.Ordinal))
            {
                level = 0; text = "TRACE"; return true;
            }
            if (s.StartsWith("DEBU", StringComparison.Ordinal))
            {
                level = 1; text = "DEBUG"; return true;
            }
            if (s.StartsWith("INFO", StringComparison.Ordinal))
            {
                level = 2; text = "INFO"; return true;
            }
            if (s.StartsWith("WARN", StringComparison.Ordinal))
            {
                level = 3; text = "WARN"; return true;
            }
            if (s.StartsWith("ERRO", StringComparison.Ordinal))
            {
                level = 4; text = "ERROR"; return true;
            }
            if (s.StartsWith("FATA", StringComparison.Ordinal))
            {
                level = 5; text = "FATAL"; return true;
            }
            if (s.StartsWith("PANI", StringComparison.Ordinal))
            {
                level = 6; text = "PANIC"; return true;
            }
        }

        if (s.Length >= 6 && s[5] == '[' && s.StartsWith("ERROR", StringComparison.Ordinal))
        {
            level = 4;
            text = "ERROR";
            return true;
        }

        return false;
    }

    private static (int Level, string Text, string Line) NormalizeSeverity(int level, string levelText, string line)
    {
        if (level < 4) return (level, levelText, line);
        if (!IsTransientConnectionClose(line)) return (level, levelText, line);

        // sing-box sometimes reports normal TCP session closes as ERRO.
        // We downgrade these noisy records to WARN to reduce false alarms in UI.
        var normalizedLine = SingBoxErrorPrefix.Replace(line, "WARN");
        return (3, "WARN", normalizedLine);
    }

    private static string StripLevelPrefix(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return line;
        var s = line.TrimStart();
        s = SingBoxLevelPrefix.Replace(s, "");
        s = BracketLevelPrefix.Replace(s, "");
        s = WordLevelPrefix.Replace(s, "");
        return s.TrimStart();
    }

    private static bool IsTransientConnectionClose(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var t = line.Trim().ToLowerInvariant();
        if (!t.Contains("connection")) return false;
        if (!t.Contains("closed")) return false;

        return t.Contains("connection upload closed")
            || t.Contains("connection download closed")
            || t.Contains("forcibly closed by the remote host")
            || t.Contains("aborted by the software in your host machine");
    }

    public void Dispose()
    {
        Stop();
    }

    internal static string? ResolveSingBoxExePathPublic(string hint) => ResolveSingBoxExePath(hint);

    private static string? TryCheckConfig(string exePath, string configPath)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"check -c \"{configPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };

            if (!p.Start())
                return "Не удалось запустить sing-box check.";

            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);

            if (p.ExitCode == 0)
                return null;

            var text = StripAnsi(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim();
            if (text.Length == 0)
                return "sing-box check завершился с ошибкой.";

            var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(l => l.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                ?? text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
                ?? text;

            return line.Length > 400 ? line[..400] + "…" : line;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
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


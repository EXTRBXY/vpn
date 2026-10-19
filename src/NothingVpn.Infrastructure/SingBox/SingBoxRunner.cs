using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Security;
using NothingVpn.Infrastructure.Windows;

namespace NothingVpn.Infrastructure.SingBox;

internal sealed class SingBoxRunner : IDisposable
{
    private static readonly TimeSpan ProcessStopTimeout = TimeSpan.FromSeconds(2);

    private readonly string _singBoxExePathHint;
    private readonly string? _installDirectory;
    private readonly InMemoryLogStore _logStore;
    private Process? _process;
    private string? _lastConfigPath;
    private ProcessJobScope? _processJob;
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
        string singBoxExePath,
        InMemoryLogStore logStore)
    {
        _singBoxExePathHint = singBoxExePath;
        _logStore = logStore;
    }

    /// <summary>
    /// Режим службы (ADR-0001): sing-box.exe и wintun.dll берутся только из каталога установки службы —
    /// без поиска по bin\, родительским каталогам и пользовательской установке.
    /// </summary>
    public static SingBoxRunner ForInstallDirectory(string installDirectory, InMemoryLogStore logStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        var directory = Path.GetFullPath(installDirectory);
        return new SingBoxRunner(Path.Combine(directory, "sing-box.exe"), logStore, directory);
    }

    private SingBoxRunner(string singBoxExePath, InMemoryLogStore logStore, string installDirectory)
        : this(singBoxExePath, logStore)
    {
        _installDirectory = installDirectory;
    }

    public void ValidateConfig(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            throw new FileNotFoundException("sing-box config not found.", configPath);

        var exePath = ResolveExePathOrThrow();

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"check -c \"{configPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exePath)!
        };

        var (exitCode, stdout, stderr) = RunProcessAsync(psi, ProcessStopTimeout)
            .GetAwaiter()
            .GetResult();

        if (exitCode == 0)
            return;

        var detail = ExtractCheckFailure(stdout, stderr);
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(detail)
                ? $"sing-box check failed (exit {exitCode})."
                : $"sing-box check failed: {detail}");
    }

    internal static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            await ObserveProcessExitAsync(process).ConfigureAwait(false);
            await ObserveOutputAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw new TimeoutException("sing-box check timed out.");
        }
        catch
        {
            TryKillProcessTree(process);
            await ObserveProcessExitAsync(process).ConfigureAwait(false);
            await ObserveOutputAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (process.ExitCode, stdout, stderr);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static async Task ObserveProcessExitAsync(Process process)
    {
        try { await process.WaitForExitAsync().ConfigureAwait(false); }
        catch { }
    }

    private static async Task ObserveOutputAsync(params Task<string>[] outputTasks)
    {
        try { await Task.WhenAll(outputTasks).ConfigureAwait(false); }
        catch { }
    }

    public void Start(string configPath)
    {
        if (UsesTunInbound(configPath) && IsRunning)
            Stop();

        lock (_gate)
        {
            if (_process is { HasExited: false })
                throw new InvalidOperationException("sing-box is already running.");

            var exePath = ResolveExePathOrThrow();

            _logStore.Clear();
            _lastConfigPath = configPath;

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
            p.Exited += OnProcessExited;

            if (!p.Start())
                throw new InvalidOperationException("Failed to start sing-box process.");

            _processJob?.Dispose();
            _processJob = ProcessJobScope.TryAttach(p);
            _process = p;

            _ = Task.Run(() => PumpAsync(p.StandardOutput, "sing-box/stdout", _logStore));
            _ = Task.Run(() => PumpAsync(p.StandardError, "sing-box/stderr", _logStore));
        }
    }

    public void TryDeleteLastConfig()
    {
        string? path;
        lock (_gate)
        {
            path = _lastConfigPath;
            _lastConfigPath = null;
        }

        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort; do not fail disconnect
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process exitedProcess)
            return;

        ProcessJobScope? job;
        lock (_gate)
        {
            if (!ReferenceEquals(_process, exitedProcess))
                return;

            job = _processJob;
            _processJob = null;
            _process = null;
        }

        job?.Dispose();
        TryDeleteLastConfig();
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        Process? proc;
        ProcessJobScope? job;
        lock (_gate)
        {
            proc = _process;
            job = _processJob;
            _process = null;
            _processJob = null;
        }

        if (proc is null)
            return;

        try
        {
            if (!proc.HasExited)
            {
                CloseProcessStreams(proc);
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch { }

                WaitForProcessExit(proc, ProcessStopTimeout);
            }
        }
        catch { }
        finally
        {
            job?.Dispose();
            try { proc.Dispose(); } catch { }
            TryDeleteLastConfig();
        }
    }

    private static void CloseProcessStreams(Process proc)
    {
        try { proc.StandardOutput?.BaseStream.Close(); } catch { }
        try { proc.StandardError?.BaseStream.Close(); } catch { }
    }

    private static void WaitForProcessExit(Process proc, TimeSpan timeout)
    {
        try
        {
            proc.WaitForExit((int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue));
        }
        catch { }
    }

    private static async Task PumpAsync(StreamReader reader, string source, InMemoryLogStore store)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                var clean = StripAnsi(line);
                var outLine = LogRedactor.Redact(clean);
                var (lvl, lvlText) = DetectLevel(outLine);
                (lvl, lvlText, outLine) = NormalizeSeverity(lvl, lvlText, outLine);
                outLine = StripLevelPrefix(outLine);
                store.AppendStructured(
                    level: lvl,
                    source: source,
                    message: outLine,
                    raw: outLine,
                    timestampUtc: DateTimeOffset.UtcNow);
            }
        }
        catch { }
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

    private static (LogLevel Level, string Text) DetectLevel(string s)
    {
        if (string.IsNullOrEmpty(s)) return (LogLevel.Info, "INFO");

        var t = s.TrimStart();

        if (TryParseFastLevel(t, out var fastLevel, out var fastText))
            return (fastLevel, fastText);

        var m = SingBoxLevelPrefix.Match(t);
        if (m.Success)
        {
            return m.Groups[1].Value switch
            {
                "TRAC" => (LogLevel.Trace, "TRACE"),
                "DEBU" => (LogLevel.Debug, "DEBUG"),
                "INFO" => (LogLevel.Info, "INFO"),
                "WARN" => (LogLevel.Warn, "WARN"),
                "ERRO" => (LogLevel.Error, "ERROR"),
                "ERROR" => (LogLevel.Error, "ERROR"),
                "FATA" => (LogLevel.Fatal, "FATAL"),
                "PANI" => (LogLevel.Panic, "PANIC"),
                _ => (LogLevel.Info, "INFO")
            };
        }

        var m2 = BracketLevelPrefix.Match(t);
        if (m2.Success)
        {
            var v = m2.Groups[1].Value.ToUpperInvariant();
            if (v == "WARNING") v = "WARN";
            return v switch
            {
                "TRACE" => (LogLevel.Trace, "TRACE"),
                "DEBUG" => (LogLevel.Debug, "DEBUG"),
                "INFO" => (LogLevel.Info, "INFO"),
                "WARN" => (LogLevel.Warn, "WARN"),
                "ERROR" => (LogLevel.Error, "ERROR"),
                "FATAL" => (LogLevel.Fatal, "FATAL"),
                "PANIC" => (LogLevel.Panic, "PANIC"),
                _ => (LogLevel.Info, "INFO")
            };
        }

        var m3 = WordLevelPrefix.Match(t);
        if (m3.Success)
        {
            var v = m3.Groups[1].Value.ToUpperInvariant();
            if (v == "WARNING") v = "WARN";
            return v switch
            {
                "TRACE" => (LogLevel.Trace, "TRACE"),
                "DEBUG" => (LogLevel.Debug, "DEBUG"),
                "INFO" => (LogLevel.Info, "INFO"),
                "WARN" => (LogLevel.Warn, "WARN"),
                "ERROR" => (LogLevel.Error, "ERROR"),
                "FATAL" => (LogLevel.Fatal, "FATAL"),
                "PANIC" => (LogLevel.Panic, "PANIC"),
                _ => (LogLevel.Info, "INFO")
            };
        }

        var m4 = LevelKeyValue.Match(t);
        if (m4.Success)
        {
            var v = m4.Groups[1].Value.ToLowerInvariant();
            return v switch
            {
                "trace" => (LogLevel.Trace, "TRACE"),
                "debug" => (LogLevel.Debug, "DEBUG"),
                "info" => (LogLevel.Info, "INFO"),
                "warn" => (LogLevel.Warn, "WARN"),
                "warning" => (LogLevel.Warn, "WARN"),
                "error" => (LogLevel.Error, "ERROR"),
                "fatal" => (LogLevel.Fatal, "FATAL"),
                "panic" => (LogLevel.Panic, "PANIC"),
                _ => (LogLevel.Info, "INFO")
            };
        }

        return (LogLevel.Info, "INFO");
    }

    private static bool TryParseFastLevel(string s, out LogLevel level, out string text)
    {
        level = LogLevel.Info;
        text = "INFO";
        if (s.Length >= 5 && s[4] == '[')
        {
            if (s.StartsWith("TRAC", StringComparison.Ordinal))
            {
                level = LogLevel.Trace; text = "TRACE"; return true;
            }
            if (s.StartsWith("DEBU", StringComparison.Ordinal))
            {
                level = LogLevel.Debug; text = "DEBUG"; return true;
            }
            if (s.StartsWith("INFO", StringComparison.Ordinal))
            {
                level = LogLevel.Info; text = "INFO"; return true;
            }
            if (s.StartsWith("WARN", StringComparison.Ordinal))
            {
                level = LogLevel.Warn; text = "WARN"; return true;
            }
            if (s.StartsWith("ERRO", StringComparison.Ordinal))
            {
                level = LogLevel.Error; text = "ERROR"; return true;
            }
            if (s.StartsWith("FATA", StringComparison.Ordinal))
            {
                level = LogLevel.Fatal; text = "FATAL"; return true;
            }
            if (s.StartsWith("PANI", StringComparison.Ordinal))
            {
                level = LogLevel.Panic; text = "PANIC"; return true;
            }
        }

        if (s.Length >= 6 && s[5] == '[' && s.StartsWith("ERROR", StringComparison.Ordinal))
        {
            level = LogLevel.Error;
            text = "ERROR";
            return true;
        }

        return false;
    }

    private static (LogLevel Level, string Text, string Line) NormalizeSeverity(LogLevel level, string levelText, string line)
    {
        if (level < LogLevel.Error) return (level, levelText, line);
        if (!IsTransientConnectionClose(line)) return (level, levelText, line);

        var normalizedLine = SingBoxErrorPrefix.Replace(line, "WARN");
        return (LogLevel.Warn, "WARN", normalizedLine);
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

    private static bool UsesTunInbound(string configPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!doc.RootElement.TryGetProperty("inbounds", out var inbounds))
                return false;

            foreach (var inbound in inbounds.EnumerateArray())
            {
                if (inbound.TryGetProperty("type", out var typeProp)
                    && string.Equals(typeProp.GetString(), "tun", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // best-effort
        }

        return false;
    }

    private static string ExtractCheckFailure(string stdout, string stderr)
    {
        static IEnumerable<string> Lines(string text) =>
            (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        string? Pick(IEnumerable<string> lines, string marker) =>
            lines.LastOrDefault(l => l.Contains(marker, StringComparison.OrdinalIgnoreCase));

        var all = Lines(stderr).Concat(Lines(stdout)).ToList();
        return Pick(all, "FATAL")
            ?? Pick(all, "ERROR")
            ?? all.LastOrDefault()?.Trim()
            ?? "";
    }

    private string ResolveExePathOrThrow()
    {
        if (_installDirectory is not null)
        {
            return ResolveInInstallDirectory(_installDirectory)
                ?? throw new FileNotFoundException(
                    $"sing-box.exe не найден в каталоге службы: {_installDirectory}",
                    _singBoxExePathHint);
        }

        return ResolveSingBoxExePath(_singBoxExePathHint)
            ?? throw new FileNotFoundException(
                "sing-box.exe not found. Put it next to the app or into a ./bin folder near it.",
                _singBoxExePathHint);
    }

    internal static string? ResolveInInstallDirectory(string installDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(installDirectory), "sing-box.exe");
        return File.Exists(path) ? path : null;
    }

    private static string? ResolveSingBoxExePath(string hint)
    {
        if (!string.IsNullOrWhiteSpace(hint) && File.Exists(hint))
            return Path.GetFullPath(hint);

        var baseDir = AppContext.BaseDirectory;
        var installedDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "NothingVpn");

        var candidates = new List<string>
        {
            Path.Combine(baseDir, hint),
            Path.Combine(baseDir, "sing-box.exe"),
            Path.Combine(baseDir, "bin", "sing-box.exe"),
            Path.Combine(installedDir, "sing-box.exe"),
        };

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 5 && dir.Parent is not null; i++)
        {
            dir = dir.Parent;
            candidates.Add(Path.Combine(dir.FullName, "sing-box.exe"));
            candidates.Add(Path.Combine(dir.FullName, "bin", "sing-box.exe"));
        }

        string? fallback = null;
        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(c))
                continue;

            var full = Path.GetFullPath(c);
            // TUN на Windows грузит wintun.dll из каталога sing-box (WorkingDirectory).
            // Предпочитаем полный комплект, чтобы publish без wintun не перекрывал установку.
            if (File.Exists(Path.Combine(Path.GetDirectoryName(full)!, "wintun.dll")))
                return full;

            fallback ??= full;
        }

        return fallback;
    }
}

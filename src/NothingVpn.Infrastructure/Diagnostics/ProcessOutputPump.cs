using System.Text.RegularExpressions;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Security;

namespace NothingVpn.Infrastructure.Diagnostics;

/// <summary>
/// Переносит вывод процесса ядра в журнал: маскирует секреты (<see cref="LogRedactor"/>), определяет уровень по
/// префиксу sing-box (<c>INFO[0000]</c>) или Xray (<c>2026/11/02 12:00:00.000000 [Warning]</c>) и убирает префикс.
/// </summary>
internal static class ProcessOutputPump
{
    public static async Task PumpAsync(StreamReader reader, string source, InMemoryLogStore store)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                var clean = XrayTimestampPrefix.Replace(StripAnsi(line), "");
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
    private static readonly Regex XrayTimestampPrefix = new(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?\s+", RegexOptions.Compiled);

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

    internal static (LogLevel Level, string Text) DetectLevel(string s)
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
            return FromWord(m2.Groups[1].Value);

        var m3 = WordLevelPrefix.Match(t);
        if (m3.Success)
            return FromWord(m3.Groups[1].Value);

        var m4 = LevelKeyValue.Match(t);
        if (m4.Success)
            return FromWord(m4.Groups[1].Value);

        return (LogLevel.Info, "INFO");
    }

    private static (LogLevel Level, string Text) FromWord(string word) => word.ToUpperInvariant() switch
    {
        "TRACE" => (LogLevel.Trace, "TRACE"),
        "DEBUG" => (LogLevel.Debug, "DEBUG"),
        "INFO" => (LogLevel.Info, "INFO"),
        "WARN" or "WARNING" => (LogLevel.Warn, "WARN"),
        "ERROR" => (LogLevel.Error, "ERROR"),
        "FATAL" => (LogLevel.Fatal, "FATAL"),
        "PANIC" => (LogLevel.Panic, "PANIC"),
        _ => (LogLevel.Info, "INFO")
    };

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

    internal static string StripLevelPrefix(string line)
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
}

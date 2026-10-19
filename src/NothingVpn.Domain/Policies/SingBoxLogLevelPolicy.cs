using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class SingBoxLogLevelPolicy
{
    public const LogLevel DefaultLevel = LogLevel.Warn;

    // Неизвестное значение исторически даёт info, а не DefaultLevel; итоговый конфиг sing-box от этого зависит.
    public static readonly SettingValueMap<LogLevel> Levels = new(
        LogLevel.Info,
        new Dictionary<LogLevel, string>
        {
            [LogLevel.Trace] = "trace",
            [LogLevel.Debug] = "debug",
            [LogLevel.Info] = "info",
            [LogLevel.Warn] = "warn",
            [LogLevel.Error] = "error",
            [LogLevel.Fatal] = "fatal",
            [LogLevel.Panic] = "panic"
        },
        new Dictionary<string, LogLevel> { ["warning"] = LogLevel.Warn });

    public static LogLevel Normalize(LogLevel level) => Levels.Normalize(level);
}

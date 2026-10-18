namespace NothingVpn.Domain.Policies;

public static class SingBoxLogLevelPolicy
{
    public const string DefaultLevel = "warn";

    // Неизвестное значение исторически даёт info, а не DefaultLevel; итоговый конфиг sing-box от этого зависит.
    private const string FallbackLevel = "info";

    public static string Normalize(string? level) =>
        (level ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "trace" => "trace",
            "debug" => "debug",
            "info" => "info",
            "warn" or "warning" => "warn",
            "error" => "error",
            "fatal" => "fatal",
            "panic" => "panic",
            _ => FallbackLevel
        };
}

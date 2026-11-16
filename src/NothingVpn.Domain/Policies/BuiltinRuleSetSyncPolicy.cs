namespace NothingVpn.Domain.Policies;

/// <summary>Глобальное автообновление встроенных .srs: включено по умолчанию, интервал 24 ч.</summary>
public static class BuiltinRuleSetSyncPolicy
{
    public const bool DefaultAutoUpdate = true;
    public const int DefaultUpdateIntervalHours = 24;
    public const int MinUpdateIntervalHours = 1;
    public const int MaxUpdateIntervalHours = 24 * 30;

    public static int NormalizeIntervalHours(int hours)
    {
        if (hours < MinUpdateIntervalHours)
            return DefaultUpdateIntervalHours;
        if (hours > MaxUpdateIntervalHours)
            return MaxUpdateIntervalHours;
        return hours;
    }

    public static bool IsDue(bool autoUpdate, DateTimeOffset? lastAutoUpdateUtc, int intervalHours, DateTimeOffset utcNow)
    {
        if (!autoUpdate)
            return false;

        if (lastAutoUpdateUtc is null)
            return true;

        var interval = NormalizeIntervalHours(intervalHours);
        return lastAutoUpdateUtc.Value.AddHours(interval) <= utcNow;
    }
}

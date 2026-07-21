namespace NothingVpn.Domain.Policies;

public static class TunAppsPolicy
{
    public static bool IsTunApps(string? mode) =>
        string.Equals(ConnectionPolicy.NormalizeMode(mode), ConnectionPolicy.TunAppsMode, StringComparison.Ordinal);

    public static bool UseStrictRoute(string? mode, bool userPreference) =>
        userPreference && !IsTunApps(mode);
}

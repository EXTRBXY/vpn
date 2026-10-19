using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class TunAppsPolicy
{
    public static bool IsTunApps(ConnectionMode mode) => mode == ConnectionMode.TunApps;

    public static bool UseStrictRoute(ConnectionMode mode, bool userPreference) =>
        userPreference && !IsTunApps(mode);
}

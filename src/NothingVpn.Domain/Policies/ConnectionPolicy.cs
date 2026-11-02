using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class ConnectionPolicy
{
    public const ConnectionMode DefaultMode = ConnectionMode.Proxy;

    // Mode of a fresh state.json (first launch): TUN, all traffic. DefaultMode stays the
    // fallback for unknown values and the idle engine status.
    public const ConnectionMode InitialMode = ConnectionMode.Tun;
    public const int DefaultLocalMixedPort = 1080;

    public static readonly SettingValueMap<ConnectionMode> Modes = new(
        DefaultMode,
        new Dictionary<ConnectionMode, string>
        {
            [ConnectionMode.Proxy] = "proxy",
            [ConnectionMode.Tun] = "tun",
            [ConnectionMode.TunApps] = "tun_apps"
        });

    public static bool IsTunMode(ConnectionMode mode) =>
        Modes.Normalize(mode) is ConnectionMode.Tun or ConnectionMode.TunApps;

    public static void EnsureTunAppsHasTargets(ConnectionMode mode, IReadOnlyCollection<string> processPaths)
    {
        if (mode != ConnectionMode.TunApps)
            return;

        if (processPaths.Count == 0)
            throw new InvalidOperationException("В режиме TUN (выбранные приложения) нужно добавить хотя бы один .exe.");
    }
}

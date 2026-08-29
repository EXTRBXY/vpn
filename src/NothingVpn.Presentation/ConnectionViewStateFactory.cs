using NothingVpn.Application.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Presentation;

public static class ConnectionViewStateFactory
{
    public static ConnectionViewState Create(
        AppStateModel state,
        VpnProfile? selectedProfile,
        bool isRunning,
        bool isAdministrator)
    {
        var mode = ConnectionPolicy.NormalizeMode(state.Mode);
        var canEditTunApps = !isRunning && TunAppsPolicy.IsTunApps(mode);
        return new ConnectionViewState(
            IsRunning: isRunning,
            CanStart: !isRunning && selectedProfile is not null,
            CanStop: isRunning,
            CanEditConnection: !isRunning,
            CanEditTunApps: canEditTunApps,
            WindowTitle: $"Nothing VPN ({TitleMode(mode)})",
            StatusText: isRunning ? "Запущено" : "Остановлено",
            AdministratorText: isAdministrator ? "Администратор" : "Обычный пользователь",
            ModeText: DisplayMode(mode),
            ProfileText: selectedProfile?.Name ?? "(не выбран)",
            PortText: state.LocalMixedPort.ToString(),
            DnsText: BuildDnsStatusText(state, mode),
            RuleSetsText: BuildRuleSetsStatusText(state),
            TunText: BuildTunStatusText(state, mode),
            ProxyBypassText: BuildProxyBypassStatusText(state, mode));
    }

    private static string TitleMode(string mode) => mode switch
    {
        ConnectionPolicy.TunMode => "TUN",
        ConnectionPolicy.TunAppsMode => "TUN (приложения)",
        _ => "прокси"
    };

    private static string DisplayMode(string mode) => mode switch
    {
        ConnectionPolicy.TunMode => "TUN (весь трафик)",
        ConnectionPolicy.TunAppsMode => "TUN (выбранные приложения)",
        _ => "Прокси"
    };

    private static string BuildDnsStatusText(AppStateModel state, string mode)
    {
        var dnsMode = (state.DnsMode ?? string.Empty).Trim().ToLowerInvariant();
        var effectiveDetour = DnsDetourPolicy.EffectiveDetour(mode, state.DnsDetour);
        var detourLabel = DnsPolicy.DetourToDisplayLabel(effectiveDetour).ToLowerInvariant();
        if (TunAppsPolicy.IsTunApps(mode))
        {
            return dnsMode == "doh"
                ? $"DoH (hijack), трафик приложений по списку, {detourLabel}"
                : "Системный DNS (hijack), трафик приложений по списку";
        }

        if (dnsMode == "doh")
        {
            var server = string.IsNullOrWhiteSpace(state.DohServer) ? "(не задан)" : state.DohServer.Trim();
            var sni = string.IsNullOrWhiteSpace(state.DohSni) ? "(без SNI)" : state.DohSni.Trim();
            return $"DoH: {server}, SNI: {sni}, {detourLabel}";
        }

        return $"Системный/по умолчанию, {detourLabel}";
    }

    private static string BuildRuleSetsStatusText(AppStateModel state)
    {
        var all = state.UserRuleSets ?? new List<UserRuleSetModel>();
        var enabled = all.Count(x => x.Enabled);
        var builtinEnabled = all.Count(x => x.Enabled && !string.IsNullOrWhiteSpace(x.BuiltinId));
        var customEnabled = all.Count(x => x.Enabled && string.IsNullOrWhiteSpace(x.BuiltinId));
        return $"{enabled} активных (встроенные: {builtinEnabled}, пользовательские: {customEnabled})";
    }

    private static string BuildTunStatusText(AppStateModel state, string mode)
    {
        if (!ConnectionPolicy.IsTunMode(mode))
            return "—";

        var mtu = TunSettingsPolicy.NormalizeMtu(state.TunMtu);
        var stack = TunSettingsPolicy.StackToDisplayLabel(state.TunStack);
        var strict = state.TunStrictRoute ? "вкл." : "выкл.";
        return $"MTU {mtu}, стек {stack}, строгая маршрутизация {strict}";
    }

    private static string BuildProxyBypassStatusText(AppStateModel state, string mode)
    {
        if (!string.Equals(mode, ConnectionPolicy.ProxyMode, StringComparison.OrdinalIgnoreCase))
            return "—";

        var value = (state.ProxyOverride ?? string.Empty).Trim();
        if (value.Length == 0)
            return ProxyConnectionPolicy.DefaultProxyOverride;
        return value.Length > 48 ? value[..48] + "…" : value;
    }
}

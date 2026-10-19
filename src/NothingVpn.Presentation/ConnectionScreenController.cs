using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Presentation;

public sealed class ConnectionScreenController : IConnectionScreenController
{
    private readonly IProfileService _profileService;
    private readonly ISettingsService _settingsService;

    public ConnectionScreenController(
        IProfileService profileService,
        ISettingsService settingsService)
    {
        _profileService = profileService;
        _settingsService = settingsService;
    }

    public ConnectionScreenSnapshot Load()
    {
        var profiles = _profileService.GetProfiles();
        var state = _settingsService.GetState();
        NormalizeCollections(state);
        var normalizedDetour = DnsDetourPolicy.EffectiveDetour(state.Mode, state.DnsDetour);
        var detourChanged = state.DnsDetour != normalizedDetour;
        state.DnsDetour = normalizedDetour;

        var selected = profiles.FirstOrDefault(p =>
            string.Equals(p.Id, state.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault();
        var selectedId = selected?.Id ?? string.Empty;
        var selectionChanged = !string.Equals(state.ActiveProfileId ?? string.Empty, selectedId, StringComparison.OrdinalIgnoreCase);
        if (selectionChanged)
            state.ActiveProfileId = selectedId;

        if (detourChanged || selectionChanged)
        {
            // DnsDetour приводит к допустимому значению сама запись (AppStateNormalizer).
            _settingsService.UpdateState(current =>
            {
                if (selectionChanged)
                    current.ActiveProfileId = selectedId;
            });
        }

        return new ConnectionScreenSnapshot(state, profiles, selected);
    }

    public void Save(AppStateModel state)
    {
        NormalizeCollections(state);
        _settingsService.UpdateState(current => CopyEditableSettings(state, current));
    }

    /// <summary>
    /// Переносит из снимка экрана только то, что пользователь редактирует в окне настроек. Выбранный профиль
    /// (меняется через <see cref="SelectProfile"/>), порт mixed-inbound, метаданные обновлений и runtime-флаги
    /// подключения берутся из актуального состояния, чтобы устаревший снимок их не затёр.
    /// </summary>
    private static void CopyEditableSettings(AppStateModel source, AppStateModel target)
    {
        target.Mode = source.Mode;
        target.TunAppProcessPaths = source.TunAppProcessPaths.ToList();
        target.UserRuleSets = source.UserRuleSets.ToList();
        target.ProxyOverride = source.ProxyOverride;
        target.TunInterfaceName = source.TunInterfaceName;
        target.TunAddressCidr = source.TunAddressCidr;
        target.TunMtu = source.TunMtu;
        target.TunStack = source.TunStack;
        target.TunAutoRoute = source.TunAutoRoute;
        target.TunStrictRoute = source.TunStrictRoute;
        target.DnsMode = source.DnsMode;
        target.DohServer = source.DohServer;
        target.DohPath = source.DohPath;
        target.DohSni = source.DohSni;
        target.DnsDetour = source.DnsDetour;
        target.DebugLogs = source.DebugLogs;
        target.SingBoxLogLevel = source.SingBoxLogLevel;
        target.CloseBehavior = source.CloseBehavior;
    }

    public void SelectMode(AppStateModel state, ConnectionMode mode)
    {
        var normalized = ConnectionPolicy.Modes.Normalize(mode);
        _settingsService.UpdateState(current => current.Mode = normalized);
        state.Mode = normalized;
        state.DnsDetour = DnsDetourPolicy.EffectiveDetour(normalized, state.DnsDetour);
    }

    public void SelectProfile(AppStateModel state, string? profileId)
    {
        var normalized = profileId?.Trim() ?? string.Empty;
        _settingsService.UpdateState(current => current.ActiveProfileId = normalized);
        state.ActiveProfileId = normalized;
    }

    private static void NormalizeCollections(AppStateModel state)
    {
        state.TunAppProcessPaths ??= new List<string>();
        state.UserRuleSets ??= new List<UserRuleSetModel>();
    }
}

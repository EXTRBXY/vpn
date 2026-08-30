using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
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
        var settingsChanged = !string.Equals(state.DnsDetour, normalizedDetour, StringComparison.Ordinal);
        state.DnsDetour = normalizedDetour;

        var selected = profiles.FirstOrDefault(p =>
            string.Equals(p.Id, state.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault();
        var selectedId = selected?.Id ?? string.Empty;
        if (!string.Equals(state.ActiveProfileId ?? string.Empty, selectedId, StringComparison.OrdinalIgnoreCase))
        {
            state.ActiveProfileId = selectedId;
            settingsChanged = true;
        }
        if (settingsChanged)
            _settingsService.SaveState(state);

        return new ConnectionScreenSnapshot(state, profiles, selected);
    }

    public void Save(AppStateModel state)
    {
        NormalizeCollections(state);
        _settingsService.SaveState(state);
    }

    public void SelectProfile(AppStateModel state, string? profileId)
    {
        state.ActiveProfileId = profileId?.Trim() ?? string.Empty;
        _settingsService.UpdateState(current => current.ActiveProfileId = state.ActiveProfileId);
    }

    private static void NormalizeCollections(AppStateModel state)
    {
        state.TunAppProcessPaths ??= new List<string>();
        state.UserRuleSets ??= new List<UserRuleSetModel>();
        if (string.IsNullOrWhiteSpace(state.DnsDetour))
            state.DnsDetour = "direct";
    }
}

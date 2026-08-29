using NothingVpn.Application.Models;
using NothingVpn.Application.Services;

namespace NothingVpn.Presentation;

public sealed class ProfileManagementController : IProfileManagementController
{
    private readonly IProfileService _profileService;
    private readonly string? _initialActiveProfileId;
    private string? _activeProfileId;

    public ProfileManagementController(IProfileService profileService, string? initialActiveProfileId)
    {
        _profileService = profileService;
        _initialActiveProfileId = NormalizeId(initialActiveProfileId);
        _activeProfileId = _initialActiveProfileId;
    }

    public ProfileManagementSnapshot Load() => Snapshot(_profileService.GetProfiles());

    public bool TryParse(string link, out VpnProfile profile) =>
        _profileService.TryParseVlessLink(link, out profile);

    public VpnProfile Add(string link, string? nameOverride)
    {
        var saved = _profileService.UpsertFromVlessLink(link, nameOverride);
        if (_activeProfileId is null)
            _activeProfileId = NormalizeId(saved.Id);
        return saved;
    }

    public VpnProfile Edit(string existingProfileId, string link, string? nameOverride)
    {
        var oldId = NormalizeId(existingProfileId)
            ?? throw new ArgumentException("Profile id is required.", nameof(existingProfileId));
        var saved = _profileService.UpsertFromVlessLink(link, nameOverride);
        if (!string.Equals(saved.Id, oldId, StringComparison.OrdinalIgnoreCase))
            _profileService.DeleteProfile(oldId);
        if (string.Equals(_activeProfileId, oldId, StringComparison.OrdinalIgnoreCase))
            _activeProfileId = NormalizeId(saved.Id);
        return saved;
    }

    public ProfileManagementSnapshot Delete(string profileId)
    {
        var deletedId = NormalizeId(profileId)
            ?? throw new ArgumentException("Profile id is required.", nameof(profileId));
        var profiles = _profileService.DeleteProfile(deletedId);
        if (string.Equals(_activeProfileId, deletedId, StringComparison.OrdinalIgnoreCase))
            _activeProfileId = NormalizeId(profiles.FirstOrDefault()?.Id);
        return Snapshot(profiles);
    }

    private ProfileManagementSnapshot Snapshot(IReadOnlyList<VpnProfile> profiles)
    {
        var changed = string.Equals(_activeProfileId, _initialActiveProfileId, StringComparison.OrdinalIgnoreCase)
            ? null
            : _activeProfileId ?? string.Empty;
        return new ProfileManagementSnapshot(profiles, _activeProfileId, changed);
    }

    private static string? NormalizeId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : id.Trim();
}

using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;

namespace NothingVpn.Application.Services;

public sealed class ProfileService(IProfileStorePort profileStore, IProfileParserPort profileParser) : IProfileService
{
    public IReadOnlyList<VpnProfile> GetProfiles() => profileStore.Load();

    public IReadOnlyList<VpnProfile> ImportFromVlessLink(string link)
    {
        var parsed = profileParser.ParseVlessLink(link);
        return profileStore.Upsert(parsed);
    }

    public IReadOnlyList<VpnProfile> DeleteProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile id is required.", nameof(profileId));

        return profileStore.Delete(profileId);
    }

    public bool TryParseVlessLink(string link, out VpnProfile profile)
    {
        profile = new VpnProfile();
        if (string.IsNullOrWhiteSpace(link))
            return false;

        try
        {
            profile = profileParser.ParseVlessLink(link);
            return true;
        }
        catch
        {
            profile = new VpnProfile();
            return false;
        }
    }

    public VpnProfile UpsertFromVlessLink(string link, string? nameOverride)
    {
        if (string.IsNullOrWhiteSpace(link))
            throw new ArgumentException("VLESS link is required.", nameof(link));

        var parsed = profileParser.ParseVlessLink(link);
        if (!string.IsNullOrWhiteSpace(nameOverride))
            parsed.Name = SanitizeName(nameOverride);

        var updated = profileStore.Upsert(parsed);
        var saved = updated.FirstOrDefault(p => string.Equals(p.Id, parsed.Id, StringComparison.OrdinalIgnoreCase));
        return saved ?? parsed;
    }

    private static string SanitizeName(string nameOverride)
    {
        var n = (nameOverride ?? "").Trim();
        if (n.Length == 0) return "Unnamed";
        return n.Length > 64 ? n[..64] : n;
    }
}


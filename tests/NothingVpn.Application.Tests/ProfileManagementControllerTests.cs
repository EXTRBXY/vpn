using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class ProfileManagementControllerTests
{
    [Fact]
    public void Add_FirstProfile_SelectsItAndReportsChange()
    {
        var service = new FakeProfileService();
        var controller = new ProfileManagementController(service, null);

        var saved = controller.Add("vless://new", "New");
        var snapshot = controller.Load();

        Assert.Equal(saved.Id, snapshot.ActiveProfileId);
        Assert.Equal(saved.Id, snapshot.ChangedActiveProfileId);
    }

    [Fact]
    public void Edit_ActiveProfileWithChangedId_ReplacesAndKeepsItActive()
    {
        var service = new FakeProfileService("old");
        var controller = new ProfileManagementController(service, "old");

        var saved = controller.Edit("old", "vless://new", null);
        var snapshot = controller.Load();

        Assert.Equal("new", saved.Id);
        Assert.Equal(new[] { "old" }, service.DeletedIds);
        Assert.Equal("new", snapshot.ActiveProfileId);
        Assert.Equal("new", snapshot.ChangedActiveProfileId);
    }

    [Fact]
    public void Delete_ActiveProfile_SelectsFirstRemainingProfile()
    {
        var service = new FakeProfileService("p1", "p2");
        var controller = new ProfileManagementController(service, "p1");

        var snapshot = controller.Delete("p1");

        Assert.Equal("p2", snapshot.ActiveProfileId);
        Assert.Equal("p2", snapshot.ChangedActiveProfileId);
    }

    private sealed class FakeProfileService : IProfileService
    {
        private readonly List<VpnProfile> _profiles;

        public FakeProfileService(params string[] ids) =>
            _profiles = ids.Select(IdToProfile).ToList();

        public List<string> DeletedIds { get; } = new();
        public IReadOnlyList<VpnProfile> GetProfiles() => _profiles;
        public IReadOnlyList<VpnProfile> ImportFromVlessLink(string link) => throw new NotSupportedException();

        public IReadOnlyList<VpnProfile> DeleteProfile(string profileId)
        {
            DeletedIds.Add(profileId);
            _profiles.RemoveAll(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
            return _profiles;
        }

        public bool TryParseVlessLink(string link, out VpnProfile profile)
        {
            profile = IdToProfile("parsed");
            return true;
        }

        public VpnProfile UpsertFromVlessLink(string link, string? nameOverride)
        {
            var profile = IdToProfile(link.EndsWith("new", StringComparison.Ordinal) ? "new" : "saved");
            profile.Name = nameOverride ?? profile.Id;
            _profiles.RemoveAll(p => p.Id == profile.Id);
            _profiles.Add(profile);
            return profile;
        }

        private static VpnProfile IdToProfile(string id) => new() { Id = id, Name = id };
    }
}

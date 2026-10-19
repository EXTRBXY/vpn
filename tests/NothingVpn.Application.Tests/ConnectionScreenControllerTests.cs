using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class ConnectionScreenControllerTests
{
    [Fact]
    public void Load_SelectsExistingActiveProfileWithoutSaving()
    {
        var profiles = new FakeProfileService("p1", "p2");
        var settings = new FakeSettingsService
        {
            State = new AppStateModel { ActiveProfileId = "p2" }
        };
        var controller = new ConnectionScreenController(profiles, settings);

        var snapshot = controller.Load();

        Assert.Equal("p2", snapshot.SelectedProfile?.Id);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public void Load_MissingActiveProfile_SelectsFirstAndPersistsSelection()
    {
        var profiles = new FakeProfileService("p1", "p2");
        var settings = new FakeSettingsService
        {
            State = new AppStateModel { ActiveProfileId = "missing" }
        };
        var controller = new ConnectionScreenController(profiles, settings);

        var snapshot = controller.Load();

        Assert.Equal("p1", snapshot.SelectedProfile?.Id);
        Assert.Equal("p1", snapshot.State.ActiveProfileId);
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void Load_NoProfiles_ClearsStaleSelectionAndNormalizesCollections()
    {
        var settings = new FakeSettingsService
        {
            State = new AppStateModel
            {
                ActiveProfileId = "missing",
                TunAppProcessPaths = null!,
                UserRuleSets = null!,
                DnsDetour = DnsDetour.Direct
            }
        };
        var controller = new ConnectionScreenController(new FakeProfileService(), settings);

        var snapshot = controller.Load();

        Assert.Null(snapshot.SelectedProfile);
        Assert.Equal(string.Empty, snapshot.State.ActiveProfileId);
        Assert.NotNull(snapshot.State.TunAppProcessPaths);
        Assert.NotNull(snapshot.State.UserRuleSets);
        Assert.Equal("direct", snapshot.State.DnsDetour.ToValue());
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void SelectMode_DoesNotOverwriteSettingsSavedSinceTheScreenLoaded()
    {
        var settings = new FakeSettingsService
        {
            State = new AppStateModel { TunAppProcessPaths = [@"C:\Apps\saved.exe"], CloseBehavior = AppCloseBehavior.Exit, ActiveProfileId = "p2" }
        };
        var stale = new AppStateModel { ActiveProfileId = "p1" };
        var controller = new ConnectionScreenController(new FakeProfileService("p1", "p2"), settings);
        controller.SelectMode(stale, ConnectionMode.TunApps);
        Assert.Equal("tun_apps", settings.State.Mode.ToValue());
        Assert.Equal(new[] { @"C:\Apps\saved.exe" }, settings.State.TunAppProcessPaths);
        Assert.Equal("exit", settings.State.CloseBehavior.ToValue());
        Assert.Equal("p2", settings.State.ActiveProfileId);
    }

    [Fact]
    public void SelectProfile_UpdatesStateAndSaves()
    {
        var settings = new FakeSettingsService { State = new AppStateModel() };
        var controller = new ConnectionScreenController(new FakeProfileService(), settings);

        controller.SelectProfile(settings.State, "  p1  ");

        Assert.Equal("p1", settings.State.ActiveProfileId);
        Assert.Equal(1, settings.SaveCalls);
    }

    private sealed class FakeProfileService : IProfileService
    {
        private readonly IReadOnlyList<VpnProfile> _profiles;

        public FakeProfileService(params string[] ids)
        {
            _profiles = ids.Select(id => new VpnProfile { Id = id, Name = id }).ToList();
        }

        public IReadOnlyList<VpnProfile> GetProfiles() => _profiles;
        public IReadOnlyList<VpnProfile> ImportFromVlessLink(string link) => throw new NotSupportedException();
        public IReadOnlyList<VpnProfile> DeleteProfile(string profileId) => throw new NotSupportedException();
        public bool TryParseVlessLink(string link, out VpnProfile profile) => throw new NotSupportedException();
        public VpnProfile UpsertFromVlessLink(string link, string? nameOverride) => throw new NotSupportedException();
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public event EventHandler<AppStateModel>? StateChanged
        {
            add { }
            remove { }
        }

        public AppStateModel State { get; set; } = new();
        public int SaveCalls { get; private set; }

        public AppStateModel GetState() => State;

        public void UpdateState(Action<AppStateModel> update)
        {
            update(State);
            SaveCalls++;
        }

        public void UpdateMode(ConnectionMode mode) => throw new NotSupportedException();
        public void UpdateDns(DnsMode mode, string dohServer, string dohPath, string dohSni, DnsDetour detour) => throw new NotSupportedException();
        public void UpdateTunSettings(TunSettings settings) => throw new NotSupportedException();
        public void UpdateProxySettings(ProxyConnectionSettings settings) => throw new NotSupportedException();
        public void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets) => throw new NotSupportedException();
        public void UpdateTunApps(IReadOnlyCollection<string> paths) => throw new NotSupportedException();
    }
}

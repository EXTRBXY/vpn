using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class AppUpdateControllerTests
{
    [Fact]
    public async Task CheckAsync_NewerRelease_ReturnsItAndRecordsCheck()
    {
        var service = new FakeUpdateService { Latest = Release("2.0.0") };
        var settings = new FakeSettingsService();
        var state = new AppStateModel();
        var controller = new AppUpdateController(service, settings);

        var result = await controller.CheckAsync(state, "1.0.0");

        Assert.True(result.Succeeded);
        Assert.Equal("2.0.0", result.AvailableRelease?.Semver);
        Assert.NotNull(state.UpdateLastCheckUtc);
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public async Task CheckAsync_CurrentRelease_ReturnsNoAvailableUpdate()
    {
        var controller = new AppUpdateController(
            new FakeUpdateService { Latest = Release("1.0.0") },
            new FakeSettingsService());

        var result = await controller.CheckAsync(new AppStateModel(), "1.0.0");

        Assert.True(result.Succeeded);
        Assert.Null(result.AvailableRelease);
    }

    [Fact]
    public async Task CheckAsync_ServiceFailure_DoesNotRecordCheck()
    {
        var settings = new FakeSettingsService();
        var controller = new AppUpdateController(new FakeUpdateService { Error = "offline" }, settings);
        var state = new AppStateModel();

        var result = await controller.CheckAsync(state, "1.0.0");

        Assert.False(result.Succeeded);
        Assert.Equal("offline", result.Error);
        Assert.Null(state.UpdateLastCheckUtc);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public void PeriodicAndDismissalPolicies_AreAppliedAndPersisted()
    {
        var settings = new FakeSettingsService();
        var controller = new AppUpdateController(new FakeUpdateService(), settings);
        var now = DateTimeOffset.UtcNow;
        var state = new AppStateModel { UpdateLastCheckUtc = now - TimeSpan.FromHours(1) };
        var release = Release("2.0.0");

        Assert.False(controller.IsPeriodicCheckDue(state, now));
        Assert.True(controller.ShouldOffer(state, release));
        controller.DismissOffer(state, release);

        Assert.False(controller.ShouldOffer(state, release));
        Assert.Equal(1, settings.SaveCalls);
    }

    [Theory]
    [InlineData(null, "1.0.0", InstalledVersionTransition.FirstRun)]
    [InlineData("1.0.0", "2.0.0", InstalledVersionTransition.Upgraded)]
    [InlineData("2.0.0", "1.0.0", InstalledVersionTransition.Downgraded)]
    [InlineData("1.0.0", "1.0.0", InstalledVersionTransition.Unchanged)]
    public void RecordInstalledVersion_ClassifiesAndPersistsChanges(
        string? previous,
        string current,
        InstalledVersionTransition expected)
    {
        var settings = new FakeSettingsService();
        var state = new AppStateModel { LastRecordedAppSemver = previous };
        var controller = new AppUpdateController(new FakeUpdateService(), settings);

        var actual = controller.RecordInstalledVersion(state, current);

        Assert.Equal(expected, actual);
        Assert.Equal(current, state.LastRecordedAppSemver);
        Assert.Equal(expected == InstalledVersionTransition.Unchanged ? 0 : 1, settings.SaveCalls);
    }

    private static AppReleaseModel Release(string version) =>
        new($"v{version}", version, null, "https://github.com/example/setup.exe");

    private sealed class FakeUpdateService : IAppUpdateService
    {
        public AppReleaseModel? Latest { get; init; }
        public string? Error { get; init; }

        public Task<AppReleaseModel?> GetLatestAsync(string currentVersion, CancellationToken cancellationToken = default)
        {
            if (Error is not null) throw new InvalidOperationException(Error);
            return Task.FromResult(Latest);
        }

        public Task<AppReleaseModel?> GetByVersionAsync(string version, CancellationToken cancellationToken = default) =>
            Task.FromResult(Latest);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public event EventHandler<AppStateModel>? StateChanged { add { } remove { } }
        public int SaveCalls { get; private set; }
        public AppStateModel GetState() => throw new NotSupportedException();
        public void SaveState(AppStateModel state) => SaveCalls++;
        public void UpdateMode(string mode) => throw new NotSupportedException();
        public void UpdateDns(string mode, string dohServer, string dohPath, string dohSni, string detour) => throw new NotSupportedException();
        public void UpdateTunSettings(TunSettings settings) => throw new NotSupportedException();
        public void UpdateProxySettings(ProxyConnectionSettings settings) => throw new NotSupportedException();
        public void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets) => throw new NotSupportedException();
        public void UpdateTunApps(IReadOnlyCollection<string> paths) => throw new NotSupportedException();
    }
}

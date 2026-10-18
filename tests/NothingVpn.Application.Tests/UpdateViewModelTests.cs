using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Desktop.Wpf;
using NothingVpn.Domain.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class UpdateViewModelTests
{
    [Fact]
    public async Task CheckAtStartupAsync_Upgraded_ShowsChangelogForCurrentVersionThenChecksUpdates()
    {
        var controller = new FakeUpdateController { Transition = InstalledVersionTransition.Upgraded };
        var shown = new List<ReleaseChangelog>();
        var viewModel = Create(controller, shown.Add);

        await viewModel.CheckAtStartupAsync();

        var changelog = Assert.Single(shown);
        Assert.Same(controller.Changelog, changelog);
        Assert.Equal(new[] { viewModel.CurrentVersion }, controller.ChangelogRequests);
        Assert.Equal(new[] { "changelog", "check" }, controller.Calls);
    }

    [Theory]
    [InlineData(InstalledVersionTransition.FirstRun)]
    [InlineData(InstalledVersionTransition.Unchanged)]
    [InlineData(InstalledVersionTransition.Downgraded)]
    public async Task CheckAtStartupAsync_NotUpgraded_DoesNotShowChangelog(InstalledVersionTransition transition)
    {
        var controller = new FakeUpdateController { Transition = transition };
        var shown = new List<ReleaseChangelog>();
        var viewModel = Create(controller, shown.Add);

        await viewModel.CheckAtStartupAsync();

        Assert.Empty(shown);
        Assert.Empty(controller.ChangelogRequests);
        Assert.Equal(new[] { "check" }, controller.Calls);
    }

    [Fact]
    public async Task CheckAtStartupAsync_FailedChangelog_IsStillShown()
    {
        var controller = new FakeUpdateController
        {
            Transition = InstalledVersionTransition.Upgraded,
            Changelog = ReleaseChangelog.Failed("2.0.0")
        };
        var shown = new List<ReleaseChangelog>();
        var viewModel = Create(controller, shown.Add);

        await viewModel.CheckAtStartupAsync();

        Assert.True(Assert.Single(shown).LoadFailed);
    }

    [Fact]
    public async Task CheckAtStartupAsync_ChangelogFailure_DoesNotBlockUpdateCheck()
    {
        var controller = new FakeUpdateController
        {
            Transition = InstalledVersionTransition.Upgraded,
            ChangelogError = new InvalidOperationException("offline")
        };
        var shown = new List<ReleaseChangelog>();
        var viewModel = Create(controller, shown.Add);

        await viewModel.CheckAtStartupAsync();

        Assert.Empty(shown);
        Assert.Equal(new[] { "changelog", "check" }, controller.Calls);
        Assert.Equal("Установлена актуальная версия.", viewModel.Status);
    }

    [Fact]
    public async Task CheckAtStartupAsync_ChangelogWindowFailure_DoesNotBlockUpdateCheck()
    {
        var controller = new FakeUpdateController { Transition = InstalledVersionTransition.Upgraded };
        var viewModel = Create(controller, _ => throw new InvalidOperationException("window"));

        await viewModel.CheckAtStartupAsync();

        Assert.Equal(new[] { "changelog", "check" }, controller.Calls);
        Assert.Equal("Установлена актуальная версия.", viewModel.Status);
    }

    [Fact]
    public async Task CheckAtStartupAsync_WithoutChangelogCallback_DoesNotLoadChangelog()
    {
        var controller = new FakeUpdateController { Transition = InstalledVersionTransition.Upgraded };
        var viewModel = Create(controller, showChangelog: null);

        await viewModel.CheckAtStartupAsync();

        Assert.Empty(controller.ChangelogRequests);
        Assert.Equal(new[] { "check" }, controller.Calls);
    }

    private static UpdateViewModel Create(FakeUpdateController controller, Action<ReleaseChangelog>? showChangelog)
    {
        var settings = new FakeSettingsService();
        return new UpdateViewModel(
            controller,
            new FakeInstallerUpdateService(),
            null!,
            settings,
            settings.GetState(),
            () => { },
            showChangelog: showChangelog);
    }

    private sealed class FakeUpdateController : IAppUpdateController
    {
        public InstalledVersionTransition Transition { get; init; }
        public ReleaseChangelog Changelog { get; init; } = ReleaseChangelog.FromRelease(
            "2.0.0",
            new AppReleaseModel("v2.0.0", "2.0.0", "Исправления", "https://github.com/example/setup.exe"));
        public Exception? ChangelogError { get; init; }
        public List<string> ChangelogRequests { get; } = [];
        public List<string> Calls { get; } = [];

        public InstalledVersionTransition RecordInstalledVersion(AppStateModel state, string currentVersion) =>
            Transition;

        public bool IsPeriodicCheckDue(AppStateModel state, DateTimeOffset utcNow) => true;

        public Task<AppUpdateCheckResult> CheckAsync(
            AppStateModel state,
            string currentVersion,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("check");
            return Task.FromResult(new AppUpdateCheckResult(true, null));
        }

        public Task<AppReleaseModel?> GetCurrentReleaseAsync(
            string currentVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReleaseChangelog> GetChangelogAsync(
            string currentVersion,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("changelog");
            ChangelogRequests.Add(currentVersion);
            if (ChangelogError is not null)
                return Task.FromException<ReleaseChangelog>(ChangelogError);
            return Task.FromResult(Changelog);
        }

        public bool ShouldOffer(AppStateModel state, AppReleaseModel release) => false;

        public void DismissOffer(AppStateModel state, AppReleaseModel release)
        {
        }
    }

    private sealed class FakeInstallerUpdateService : IInstallerUpdateService
    {
        public string GetCachedInstallerPath(string version) => throw new NotSupportedException();
        public bool IsCached(string version) => false;

        public void CleanupOldInstallers()
        {
        }

        public Task<InstallerDownloadResult> DownloadAsync(
            AppReleaseModel release,
            IProgress<InstallerDownloadProgressModel>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private readonly AppStateModel _state = new();
        public event EventHandler<AppStateModel>? StateChanged { add { } remove { } }
        public AppStateModel GetState() => _state;
        public void UpdateState(Action<AppStateModel> update) => update(_state);
        public void UpdateMode(string mode) => throw new NotSupportedException();
        public void UpdateDns(string mode, string dohServer, string dohPath, string dohSni, string detour) => throw new NotSupportedException();
        public void UpdateTunSettings(TunSettings settings) => throw new NotSupportedException();
        public void UpdateProxySettings(ProxyConnectionSettings settings) => throw new NotSupportedException();
        public void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets) => throw new NotSupportedException();
        public void UpdateTunApps(IReadOnlyCollection<string> paths) => throw new NotSupportedException();
    }
}

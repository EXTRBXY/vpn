using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Updates;

namespace NothingVpn.Presentation;

public sealed class AppUpdateController : IAppUpdateController
{
    private static readonly TimeSpan PeriodicCheckInterval = TimeSpan.FromHours(23.5);
    private readonly IAppUpdateService _updateService;
    private readonly ISettingsService _settingsService;

    public AppUpdateController(IAppUpdateService updateService, ISettingsService settingsService)
    {
        _updateService = updateService;
        _settingsService = settingsService;
    }

    public InstalledVersionTransition RecordInstalledVersion(AppStateModel state, string currentVersion)
    {
        var previousVersion = state.LastRecordedAppSemver;
        if (string.Equals(previousVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
            return InstalledVersionTransition.Unchanged;

        var transition = string.IsNullOrWhiteSpace(previousVersion)
            ? InstalledVersionTransition.FirstRun
            : SemanticVersionPolicy.Compare(currentVersion, previousVersion) > 0
                ? InstalledVersionTransition.Upgraded
                : InstalledVersionTransition.Downgraded;

        state.LastRecordedAppSemver = currentVersion;
        _settingsService.SaveState(state);
        return transition;
    }

    public bool IsPeriodicCheckDue(AppStateModel state, DateTimeOffset utcNow) =>
        state.UpdateLastCheckUtc is not { } last || utcNow - last >= PeriodicCheckInterval;

    public async Task<AppUpdateCheckResult> CheckAsync(
        AppStateModel state,
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latest = await _updateService.GetLatestAsync(currentVersion, cancellationToken).ConfigureAwait(false);
            state.UpdateLastCheckUtc = DateTimeOffset.UtcNow;
            _settingsService.SaveState(state);
            var available = latest is not null &&
                            SemanticVersionPolicy.Compare(latest.Semver, currentVersion) > 0
                ? latest
                : null;
            return new AppUpdateCheckResult(true, available);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AppUpdateCheckResult(false, null, ex.Message);
        }
    }

    public Task<AppReleaseModel?> GetCurrentReleaseAsync(
        string currentVersion,
        CancellationToken cancellationToken = default) =>
        _updateService.GetByVersionAsync(currentVersion, cancellationToken);

    public bool ShouldOffer(AppStateModel state, AppReleaseModel release) =>
        !string.Equals(state.UpdateDismissedModalForTag, release.TagName, StringComparison.OrdinalIgnoreCase);

    public void DismissOffer(AppStateModel state, AppReleaseModel release)
    {
        state.UpdateDismissedModalForTag = release.TagName;
        _settingsService.SaveState(state);
    }
}

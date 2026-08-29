using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public interface IAppUpdateController
{
    bool IsPeriodicCheckDue(AppStateModel state, DateTimeOffset utcNow);
    Task<AppUpdateCheckResult> CheckAsync(
        AppStateModel state,
        string currentVersion,
        CancellationToken cancellationToken = default);
    Task<AppReleaseModel?> GetCurrentReleaseAsync(string currentVersion, CancellationToken cancellationToken = default);
    bool ShouldOffer(AppStateModel state, AppReleaseModel release);
    void DismissOffer(AppStateModel state, AppReleaseModel release);
}

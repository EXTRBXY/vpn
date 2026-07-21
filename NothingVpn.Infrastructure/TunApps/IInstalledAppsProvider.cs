namespace NothingVpn.Infrastructure.TunApps;

internal interface IInstalledAppsProvider
{
    Task<IReadOnlyList<AppCandidate>> GetInstalledAppsAsync(CancellationToken cancellationToken);
}

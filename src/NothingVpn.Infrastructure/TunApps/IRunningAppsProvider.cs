namespace NothingVpn.Infrastructure.TunApps;

internal interface IRunningAppsProvider
{
    Task<IReadOnlyList<AppCandidate>> GetRunningAppsAsync(CancellationToken cancellationToken);
}

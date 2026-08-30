namespace NothingVpn.Infrastructure.TunApps;

public interface IRunningAppsProvider
{
    Task<IReadOnlyList<AppCandidate>> GetRunningAppsAsync(CancellationToken cancellationToken);
}

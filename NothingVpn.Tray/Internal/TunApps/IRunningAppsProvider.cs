namespace NothingVpn.Tray.Internal.TunApps;

internal interface IRunningAppsProvider
{
    Task<IReadOnlyList<AppCandidate>> GetRunningAppsAsync(CancellationToken cancellationToken);
}

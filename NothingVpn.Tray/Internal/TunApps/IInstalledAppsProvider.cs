namespace NothingVpn.Tray.Internal.TunApps;

internal interface IInstalledAppsProvider
{
    Task<IReadOnlyList<AppCandidate>> GetInstalledAppsAsync(CancellationToken cancellationToken);
}

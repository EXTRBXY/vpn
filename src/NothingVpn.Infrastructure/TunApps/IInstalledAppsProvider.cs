using NothingVpn.Application.Models;

namespace NothingVpn.Infrastructure.TunApps;

public interface IInstalledAppsProvider
{
    Task<IReadOnlyList<AppCandidate>> GetInstalledAppsAsync(CancellationToken cancellationToken);
}

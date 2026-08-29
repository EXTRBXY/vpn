using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface IAppUpdateService
{
    Task<AppReleaseModel?> GetLatestAsync(string currentVersion, CancellationToken cancellationToken = default);
    Task<AppReleaseModel?> GetByVersionAsync(string version, CancellationToken cancellationToken = default);
}

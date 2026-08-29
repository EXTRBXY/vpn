using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface IInstallerUpdateService
{
    string GetCachedInstallerPath(string version);
    bool IsCached(string version);
    void CleanupOldInstallers();
    Task<InstallerDownloadResult> DownloadAsync(
        AppReleaseModel release,
        IProgress<InstallerDownloadProgressModel>? progress = null,
        CancellationToken cancellationToken = default);
}

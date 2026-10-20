using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface IInstallerUpdateService
{
    void CleanupOldInstallers();
    Task<InstallerDownloadResult> DownloadAsync(
        AppReleaseModel release,
        IProgress<InstallerDownloadProgressModel>? progress = null,
        CancellationToken cancellationToken = default);
}

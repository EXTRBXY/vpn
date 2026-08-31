namespace NothingVpn.Application.Services;

public interface IInstallerLaunchService
{
    void EnsureLaunchAllowed();
    void ScheduleAfterApplicationExits(string installerPath);
}

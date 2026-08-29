namespace NothingVpn.Application.Services;

public interface IInstallerLaunchService
{
    void ScheduleAfterApplicationExits(string installerPath);
}

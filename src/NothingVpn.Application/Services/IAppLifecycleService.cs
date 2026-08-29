namespace NothingVpn.Application.Services;

public interface IAppLifecycleService
{
    bool IsAdministrator();
    bool RestartElevated(string arguments);
    string BuildTakeoverArgs(string mode, string profileId);
}


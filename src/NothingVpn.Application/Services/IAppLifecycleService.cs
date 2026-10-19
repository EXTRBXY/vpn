using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Services;

public interface IAppLifecycleService
{
    bool IsAdministrator();
    bool RestartElevated(string arguments);
    string BuildTakeoverArgs(ConnectionMode mode, string profileId);
}


using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Services;

public sealed class AppLifecycleService(IElevationPort elevationPort) : IAppLifecycleService
{
    public bool IsAdministrator() => elevationPort.IsAdministrator();

    public bool RestartElevated(string arguments) => elevationPort.RestartElevated(arguments);

    public string BuildTakeoverArgs(ConnectionMode mode, string profileId) =>
        StartupArguments.Takeover(mode, profileId);
}

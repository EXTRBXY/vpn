using NothingVpn.Application.Ports;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public sealed class AppLifecycleService(IElevationPort elevationPort) : IAppLifecycleService
{
    public bool IsAdministrator() => elevationPort.IsAdministrator();

    public bool RestartElevated(string arguments) => elevationPort.RestartElevated(arguments);

    public string BuildTakeoverArgs(string mode, string profileId)
    {
        var normalizedMode = ConnectionPolicy.NormalizeMode(mode);
        return $"--takeover --start --mode {normalizedMode} --profile \"{profileId}\"";
    }
}


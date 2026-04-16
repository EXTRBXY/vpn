using NothingVpn.Application.Ports;
using NothingVpn.Tray.Internal.Windows;

namespace NothingVpn.Infrastructure.Ports;

public sealed class ElevationPort : IElevationPort
{
    public bool IsAdministrator() => Elevation.IsAdministrator();
    public bool RestartElevated(string arguments) => Elevation.RestartElevated(arguments);
}


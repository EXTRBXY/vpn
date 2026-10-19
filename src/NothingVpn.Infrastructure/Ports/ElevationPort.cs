using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Windows;

namespace NothingVpn.Infrastructure.Ports;

public sealed class ElevationPort : IElevationPort
{
    public bool IsAdministrator() => Elevation.IsAdministrator();
}

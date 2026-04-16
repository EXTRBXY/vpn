namespace NothingVpn.Application.Ports;

public interface IElevationPort
{
    bool IsAdministrator();
    bool RestartElevated(string arguments);
}


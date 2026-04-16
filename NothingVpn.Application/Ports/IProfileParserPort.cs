using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface IProfileParserPort
{
    VpnProfile ParseVlessLink(string link);
}


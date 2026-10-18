using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Profile;

namespace NothingVpn.Infrastructure.Ports;

public sealed class ProfileParserPort : IProfileParserPort
{
    public VpnProfile ParseVlessLink(string link) => VlessLinkParser.Parse(link);
}


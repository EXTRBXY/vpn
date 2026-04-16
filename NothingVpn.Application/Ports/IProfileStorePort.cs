using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface IProfileStorePort
{
    IReadOnlyList<VpnProfile> Load();
    IReadOnlyList<VpnProfile> Upsert(VpnProfile profile);
}


using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Mappers;
using NothingVpn.Tray.Internal.Store;

namespace NothingVpn.Infrastructure.Ports;

public sealed class ProfileStorePort : IProfileStorePort
{
    private readonly JsonProfileStore _store;

    public ProfileStorePort(IAppPathsPort appPathsPort)
    {
        _store = new JsonProfileStore(appPathsPort.Get().ProfilesJsonPath);
    }

    public IReadOnlyList<VpnProfile> Load() => _store.Load().Select(LegacyModelMapper.ToModel).ToList();

    public IReadOnlyList<VpnProfile> Upsert(VpnProfile profile)
        => _store.Upsert(LegacyModelMapper.ToLegacy(profile)).Select(LegacyModelMapper.ToModel).ToList();
}


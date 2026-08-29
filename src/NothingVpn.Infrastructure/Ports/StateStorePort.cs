using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Mappers;
using NothingVpn.Infrastructure.RuleSets;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Infrastructure.Ports;

public sealed class StateStorePort : IStateStorePort
{
    private readonly JsonStateStore _store;

    public StateStorePort(IAppPathsPort appPathsPort)
    {
        _store = new JsonStateStore(appPathsPort.Get().StateJsonPath);
    }

    public AppStateModel Load()
    {
        var legacy = _store.Load();
        if (BuiltinGeositeRuleSets.EnsureBuiltinGeositeRuleSets(legacy))
            _store.Save(legacy);
        return LegacyModelMapper.ToModel(legacy);
    }

    public void Save(AppStateModel state) => _store.Save(LegacyModelMapper.ToLegacy(state));
}


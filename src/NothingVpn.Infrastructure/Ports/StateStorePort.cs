using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
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
        var state = _store.Load();
        BuiltinGeositeRuleSets.EnsureBuiltinGeositeRuleSets(state);
        return state;
    }

    public void Save(AppStateModel state) => _store.Save(state);

    public bool SaveMissingBuiltinRuleSets()
    {
        var state = _store.Load();
        if (!BuiltinGeositeRuleSets.EnsureBuiltinGeositeRuleSets(state))
            return false;

        _store.Save(state);
        return true;
    }
}

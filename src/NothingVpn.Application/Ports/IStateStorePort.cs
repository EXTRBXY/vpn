using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface IStateStorePort
{
    AppStateModel Load();
    void Save(AppStateModel state);
}


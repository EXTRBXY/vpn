using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public interface IConnectionSettingsController
{
    void Save(AppStateModel state, ConnectionSettingsDraft draft);
}

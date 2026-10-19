using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Presentation;

public interface IConnectionScreenController
{
    ConnectionScreenSnapshot Load();
    void Save(AppStateModel state);
    void SelectProfile(AppStateModel state, string? profileId);
    void SelectMode(AppStateModel state, ConnectionMode mode);
}

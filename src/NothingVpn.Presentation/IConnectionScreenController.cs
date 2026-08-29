using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public interface IConnectionScreenController
{
    ConnectionScreenSnapshot Load();
    void Save(AppStateModel state);
    void SelectProfile(AppStateModel state, string? profileId);
}

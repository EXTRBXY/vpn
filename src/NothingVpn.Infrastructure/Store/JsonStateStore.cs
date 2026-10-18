using System.Text.Json;
using NothingVpn.Application.Models;
using NothingVpn.Infrastructure.Security;

namespace NothingVpn.Infrastructure.Store;

internal sealed class JsonStateStore
{
    private readonly string _path;

    public JsonStateStore(string path) => _path = path;

    public AppStateModel Load()
    {
        return DpapiJsonFile.LoadOrDefault(_path, defaultFactory: () => new AppStateModel());
    }

    public void Save(AppStateModel state)
    {
        DpapiJsonFile.Save(_path, state);
    }
}


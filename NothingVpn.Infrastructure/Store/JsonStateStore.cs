using System.Text.Json;
using NothingVpn.Infrastructure.Security;

namespace NothingVpn.Infrastructure.Store;

internal sealed class JsonStateStore
{
    private readonly string _path;

    public JsonStateStore(string path) => _path = path;

    public AppState Load()
    {
        return DpapiJsonFile.LoadOrDefault(_path, defaultFactory: () => new AppState());
    }

    public void Save(AppState state)
    {
        DpapiJsonFile.Save(_path, state);
    }
}


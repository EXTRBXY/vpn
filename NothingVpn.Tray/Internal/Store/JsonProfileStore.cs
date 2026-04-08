using System.Text.Json;
using NothingVpn.Tray.Internal.Profile;
using NothingVpn.Tray.Internal.Security;

namespace NothingVpn.Tray.Internal.Store;

internal sealed class JsonProfileStore
{
    private readonly string _path;

    public JsonProfileStore(string path) => _path = path;

    public IReadOnlyList<VlessProfile> Load()
    {
        return DpapiJsonFile.LoadOrDefault(_path, defaultFactory: () => new List<VlessProfile>());
    }

    public IReadOnlyList<VlessProfile> Upsert(VlessProfile profile)
    {
        var list = Load().ToList();
        var idx = list.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) list[idx] = profile;
        else list.Add(profile);

        Save(list);
        return list;
    }

    private void Save(List<VlessProfile> profiles)
    {
        DpapiJsonFile.Save(_path, profiles);
    }
}


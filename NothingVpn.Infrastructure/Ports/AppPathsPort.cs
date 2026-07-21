using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Infrastructure.Ports;

public sealed class AppPathsPort : IAppPathsPort
{
    public AppPathsModel Get()
    {
        var paths = AppPaths.CreateDefault();
        Directory.CreateDirectory(paths.BaseDir);
        Directory.CreateDirectory(paths.ConfigsDir);
        Directory.CreateDirectory(paths.RuleSetsDir);

        return new AppPathsModel
        {
            BaseDir = paths.BaseDir,
            ConfigsDir = paths.ConfigsDir,
            RuleSetsDir = paths.RuleSetsDir,
            LogsDir = paths.LogsDir,
            ProfilesJsonPath = paths.ProfilesJsonPath,
            SubscriptionsJsonPath = paths.SubscriptionsJsonPath,
            StateJsonPath = paths.StateJsonPath
        };
    }
}


using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Infrastructure.Runtime;

internal sealed class LegacyRuntimeContext
{
    internal AppPaths Paths { get; }
    internal InMemoryLogStore LogStore { get; }
    internal SingBoxRunner Runner { get; }

    public LegacyRuntimeContext(IAppPathsPort appPathsPort, InMemoryLogStore logStore)
    {
        ArgumentNullException.ThrowIfNull(logStore);
        var model = appPathsPort.Get();
        Paths = new AppPaths(
            model.BaseDir,
            model.ConfigsDir,
            model.RuleSetsDir,
            model.LogsDir,
            model.ProfilesJsonPath,
            model.SubscriptionsJsonPath,
            model.StateJsonPath);

        LogStore = logStore;
        Runner = new SingBoxRunner("sing-box.exe", LogStore);
    }
}

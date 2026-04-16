using NothingVpn.Application.Ports;
using NothingVpn.Tray.Internal.Diagnostics;
using NothingVpn.Tray.Internal.SingBox;
using NothingVpn.Tray.Internal.Store;

namespace NothingVpn.Infrastructure.Runtime;

internal sealed class LegacyRuntimeContext
{
    internal AppPaths Paths { get; }
    internal InMemoryLogStore LogStore { get; }
    internal SingBoxRunner Runner { get; }

    public LegacyRuntimeContext(IAppPathsPort appPathsPort)
    {
        var model = appPathsPort.Get();
        Paths = new AppPaths(
            model.BaseDir,
            model.ConfigsDir,
            model.RuleSetsDir,
            model.LogsDir,
            model.ProfilesJsonPath,
            model.StateJsonPath);

        var stateStore = new JsonStateStore(Paths.StateJsonPath);
        AppState? stateSnapshot = null;
        LogStore = new InMemoryLogStore(maxBytes: 1_000_000);
        Runner = new SingBoxRunner(
            Paths,
            "sing-box.exe",
            LogStore,
            debugLogs: () => (stateSnapshot ??= stateStore.Load()).DebugLogs,
            trustedSha256: () => (stateSnapshot ??= stateStore.Load()).TrustedSingBoxSha256);
    }
}


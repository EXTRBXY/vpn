namespace NothingVpn.Tray.Internal.SingBox;

internal static class VpnRuntimeRecovery
{
    internal const string TunInterfacePrefix = "NothingVpn";
    private static readonly object RecoveryLock = new();

    public static void RecoverOnStartup(string singBoxExePathHint)
    {
        if (!OperatingSystem.IsWindows())
            return;

        lock (RecoveryLock)
        {
            var exePath = SingBoxRunner.ResolveSingBoxExePathPublic(singBoxExePathHint);
            if (exePath is not null)
                SingBoxProcessCleaner.StopFromInstallDirectory(exePath);

            ReleaseAdaptersWithPrefix(TunInterfacePrefix);
        }
    }

    public static void PrepareTunConnect(string singBoxExePath, string configPath)
    {
        if (!OperatingSystem.IsWindows())
            return;

        lock (RecoveryLock)
        {
            SingBoxProcessCleaner.StopFromInstallDirectory(singBoxExePath);

            var interfaceName = TunInterfaceCleaner.TryReadInterfaceName(configPath);
            if (!string.IsNullOrWhiteSpace(interfaceName))
                ReleaseAdapter(interfaceName);
        }
    }

    internal static void ReleaseAdapterByName(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
            return;

        lock (RecoveryLock)
        {
            ReleaseAdapter(interfaceName);
        }
    }

    private static void ReleaseAdaptersWithPrefix(string prefix)
    {
        if (WintunNative.IsAvailable)
        {
            foreach (var name in WintunNative.ListAdapterNames())
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    ReleaseAdapter(name);
            }

            return;
        }

        TunInterfaceCleaner.TryDisableAdaptersWithPrefix(prefix);
    }

    private static void ReleaseAdapter(string interfaceName)
    {
        if (WintunNative.IsAvailable && WintunNative.TryDeleteAdapter(interfaceName))
            return;

        TunInterfaceCleaner.TryDisableAdapter(interfaceName);
    }
}

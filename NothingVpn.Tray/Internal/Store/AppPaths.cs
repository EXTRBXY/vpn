namespace NothingVpn.Tray.Internal.Store;

internal sealed record AppPaths(
    string BaseDir,
    string ConfigsDir,
    string LogsDir,
    string ProfilesJsonPath,
    string StateJsonPath)
{
    public static AppPaths CreateDefault()
    {
        var baseDir = ResolveBaseDir();
        return new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: Path.Combine(baseDir, "profiles.json"),
            StateJsonPath: Path.Combine(baseDir, "state.json")
        );
    }

    private static string ResolveBaseDir()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "NothingVpn.Tray");
        }

        var fallback = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return Path.Combine(fallback, "NothingVpn.Tray");
        }

        return Path.Combine(AppContext.BaseDirectory, ".data");
    }
}


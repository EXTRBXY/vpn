namespace NothingVpn.Infrastructure.Store;

internal sealed record AppPaths(
    string BaseDir,
    string ConfigsDir,
    string RuleSetsDir,
    string LogsDir,
    string ProfilesJsonPath,
    string SubscriptionsJsonPath,
    string StateJsonPath)
{
    public static AppPaths CreateDefault()
    {
        var baseDir = ResolveBaseDir();
        return new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            RuleSetsDir: Path.Combine(baseDir, "rulesets"),
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: Path.Combine(baseDir, "profiles.json"),
            SubscriptionsJsonPath: Path.Combine(baseDir, "subscriptions.json"),
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


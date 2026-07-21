using Microsoft.Win32;

namespace NothingVpn.Infrastructure.TunApps;

internal sealed class AppPathsRegistryProvider : IInstalledAppsProvider
{
    private const string AppPathsSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    public Task<IReadOnlyList<AppCandidate>> GetInstalledAppsAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<AppCandidate>>(() =>
        {
            var result = new List<AppCandidate>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReadAppPaths(hive, view, seenPaths, result, cancellationToken);
                }
            }

            return result;
        }, cancellationToken);
    }

    private static void ReadAppPaths(
        RegistryHive hive,
        RegistryView view,
        HashSet<string> seenPaths,
        List<AppCandidate> result,
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var appPaths = baseKey.OpenSubKey(AppPathsSubKey, false);
            if (appPaths is null)
                return;

            foreach (var subKeyName in appPaths.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!subKeyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var exeKey = appPaths.OpenSubKey(subKeyName, false);
                var rawPath = exeKey?.GetValue("") as string;
                if (string.IsNullOrWhiteSpace(rawPath))
                    continue;

                var path = rawPath.Trim().Trim('"');
                if (!TunAppPathPolicy.TryNormalizeExePath(path, out var normalizedPath))
                    continue;
                if (TunAppPathPolicy.IsLikelyUninstallerExe(normalizedPath))
                    continue;
                if (!seenPaths.Add(normalizedPath))
                    continue;

                var displayName = Path.GetFileNameWithoutExtension(subKeyName);
                if (displayName.Length == 0)
                    displayName = normalizedPath;

                result.Add(new AppCandidate(displayName, normalizedPath, AppCandidateSource.Installed));
            }
        }
        catch
        {
            // Ignore inaccessible registry views/hives.
        }
    }
}

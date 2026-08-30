using System.Runtime.InteropServices;

namespace NothingVpn.Infrastructure.TunApps;

public sealed class StartMenuShortcutAppsProvider : IInstalledAppsProvider
{
    private const int MaxShortcutScanDepth = 5;

    public Task<IReadOnlyList<AppCandidate>> GetInstalledAppsAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<AppCandidate>>(() =>
        {
            var result = new List<AppCandidate>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in GetShortcutRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var lnk in EnumerateShortcutFiles(root, MaxShortcutScanDepth, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = TryResolveShortcutTarget(lnk);
                    if (!TunAppPathPolicy.TryNormalizeExePath(target, out var normalizedPath))
                        continue;
                    if (TunAppPathPolicy.IsLikelyUninstallerExe(normalizedPath))
                        continue;
                    if (!seenPaths.Add(normalizedPath))
                        continue;

                    var displayName = Path.GetFileNameWithoutExtension(lnk);
                    if (displayName.Length == 0)
                        displayName = Path.GetFileNameWithoutExtension(normalizedPath);

                    result.Add(new AppCandidate(displayName, normalizedPath, AppCandidateSource.Installed));
                }
            }

            return result;
        }, cancellationToken);
    }

    private static IEnumerable<string> GetShortcutRoots()
    {
        foreach (var folder in new[]
        {
            Environment.SpecialFolder.Programs,
            Environment.SpecialFolder.CommonPrograms,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.CommonDesktopDirectory
        })
        {
            string path;
            try
            {
                path = Environment.GetFolderPath(folder);
            }
            catch
            {
                continue;
            }

            if (path.Length > 0 && Directory.Exists(path))
                yield return path;
        }
    }

    private static IEnumerable<string> EnumerateShortcutFiles(string root, int maxDepth, CancellationToken cancellationToken)
    {
        if (maxDepth <= 0 || !Directory.Exists(root))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var f in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return f;
        }

        if (maxDepth <= 1)
            yield break;

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(root);
        }
        catch
        {
            yield break;
        }

        foreach (var dir in dirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var nested in EnumerateShortcutFiles(dir, maxDepth - 1, cancellationToken))
                yield return nested;
        }
    }

    private static string? TryResolveShortcutTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                try
                {
                    string? target = shortcut.TargetPath;
                    return string.IsNullOrWhiteSpace(target) ? null : target;
                }
                finally
                {
                    _ = Marshal.ReleaseComObject(shortcut);
                }
            }
            finally
            {
                _ = Marshal.ReleaseComObject(shell);
            }
        }
        catch
        {
            return null;
        }
    }
}

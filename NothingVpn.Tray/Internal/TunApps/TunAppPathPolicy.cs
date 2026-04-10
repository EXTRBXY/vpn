namespace NothingVpn.Tray.Internal.TunApps;

internal static class TunAppPathPolicy
{
    public static bool IsLikelyUninstallerExe(string fullPath)
    {
        var n = Path.GetFileNameWithoutExtension(fullPath);
        if (n.Length == 0)
            return true;
        if (n.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            return true;
        if (n.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
            return true;
        if (n.EndsWith("uninstall", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static bool TryNormalizeExePath(string? rawPath, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        var path = (rawPath ?? string.Empty).Trim().Trim('"');
        if (path.Length == 0)
            return false;

        if (!Path.IsPathRooted(path))
            return false;

        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return false;

            normalizedPath = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static List<string> NormalizeDistinctPaths(IEnumerable<string>? paths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var raw in paths ?? Array.Empty<string>())
        {
            if (!TryNormalizeExePath(raw, out var normalized))
                continue;
            if (!set.Add(normalized))
                continue;
            result.Add(normalized);
        }

        return result;
    }
}

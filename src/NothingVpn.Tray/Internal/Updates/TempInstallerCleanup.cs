namespace NothingVpn.Tray.Internal.Updates;

internal static class TempInstallerCleanup
{
    internal static void DeleteOldInstallersInTemp()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "NothingVpn");
            if (!Directory.Exists(dir))
                return;
            foreach (var path in Directory.EnumerateFiles(dir, "NothingVpnSetup-*.exe", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // ignore locked files
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    internal static string GetInstallerTempPath(string semver)
    {
        var dir = Path.Combine(Path.GetTempPath(), "NothingVpn");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"NothingVpnSetup-{semver}.exe");
    }
}

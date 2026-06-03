using System.Diagnostics;

namespace NothingVpn.Tray.Internal.SingBox;

internal static class SingBoxProcessCleaner
{
    public static void StopFromInstallDirectory(string singBoxExePath, Process? except = null)
    {
        var installDir = Path.GetDirectoryName(Path.GetFullPath(singBoxExePath));
        if (string.IsNullOrWhiteSpace(installDir))
            return;

        var exceptId = except is { HasExited: false } ? except.Id : -1;

        foreach (var process in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                if (process.Id == exceptId)
                    continue;

                if (!TryGetProcessPath(process, out var path))
                    continue;

                var processDir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (string.IsNullOrWhiteSpace(processDir))
                    continue;

                if (!string.Equals(processDir, installDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch
            {
                // best-effort
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool TryGetProcessPath(Process process, out string path)
    {
        path = string.Empty;
        try
        {
            path = process.MainModule?.FileName ?? string.Empty;
            return path.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}

using System.Diagnostics;
using System.Security.Principal;

namespace NothingVpn.Tray.Internal.Windows;

internal static class Elevation
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool RestartElevated(string arguments)
    {
        var exe = Application.ExecutablePath;
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}


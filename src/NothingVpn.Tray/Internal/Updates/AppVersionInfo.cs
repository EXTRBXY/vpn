using System.Diagnostics;
using System.Reflection;

namespace NothingVpn.Tray.Internal.Updates;

internal static class AppVersionInfo
{
    private const string TrayExeFileName = "NothingVpn.Tray.exe";

    internal static bool TryGetCurrentSemver(out string semver)
    {
        semver = "";

        string? raw = null;
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            string.Equals(Path.GetFileName(processPath), TrayExeFileName, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(processPath);
                raw = vi.ProductVersion;
                if (string.IsNullOrWhiteSpace(raw))
                    raw = vi.FileVersion;
            }
            catch
            {
                // ignore
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            var asm = Assembly.GetExecutingAssembly();
            raw = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(raw))
                raw = asm.GetName().Version?.ToString(3);
        }

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();
        var plus = raw.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            raw = raw[..plus].Trim();
        var space = raw.IndexOf(' ', StringComparison.Ordinal);
        if (space >= 0)
            raw = raw[..space].Trim();

        if (!SemVerComparer.TryParse(raw, out _, out _, out _))
            return false;
        semver = SemVerComparer.NormalizeToString(raw);
        return true;
    }
}

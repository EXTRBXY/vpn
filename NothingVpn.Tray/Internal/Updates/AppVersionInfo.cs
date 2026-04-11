using System.Reflection;

namespace NothingVpn.Tray.Internal.Updates;

internal static class AppVersionInfo
{
    internal static bool TryGetCurrentSemver(out string semver)
    {
        semver = "";
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
            info = asm.GetName().Version?.ToString(3);
        if (string.IsNullOrWhiteSpace(info))
            return false;

        var raw = info.Trim();
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

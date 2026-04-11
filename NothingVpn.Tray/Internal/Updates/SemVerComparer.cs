using System.Text.RegularExpressions;

namespace NothingVpn.Tray.Internal.Updates;

internal static class SemVerComparer
{
    private static readonly Regex Triple = new(
        @"^(?:v|V)?(?<a>\d+)\.(?<b>\d+)(?:\.(?<c>\d+))?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool TryParse(string? input, out int major, out int minor, out int patch)
    {
        major = minor = patch = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;
        var s = input.Trim();
        var m = Triple.Match(s);
        if (!m.Success)
            return false;
        major = int.Parse(m.Groups["a"].Value, System.Globalization.CultureInfo.InvariantCulture);
        minor = int.Parse(m.Groups["b"].Value, System.Globalization.CultureInfo.InvariantCulture);
        patch = m.Groups["c"].Success
            ? int.Parse(m.Groups["c"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : 0;
        return true;
    }

    /// <summary>Возвращает нормализованную строку Major.Minor.Patch.</summary>
    internal static string NormalizeToString(string? input)
    {
        return TryParse(input, out var ma, out var mi, out var p)
            ? FormattableString.Invariant($"{ma}.{mi}.{p}")
            : "";
    }

    /// <summary>Тег релиза на GitHub в формате vX.Y.Z для нормализованной semver без префикса.</summary>
    internal static string ToProbableGitTag(string semverNormalized)
    {
        if (string.IsNullOrWhiteSpace(semverNormalized))
            return "";
        var s = semverNormalized.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            return s;
        return "v" + s;
    }

    /// <summary>&lt;0 если a &lt; b, 0 если равны, &gt;0 если a &gt; b. Нераспознаваемые версии считаются меньше любой распознаваемой.</summary>
    internal static int CompareSemver(string? a, string? b)
    {
        var okA = TryParse(a, out var ma, out var mia, out var pa);
        var okB = TryParse(b, out var mb, out var mib, out var pb);
        if (!okA && !okB) return 0;
        if (!okA) return -1;
        if (!okB) return 1;
        var c = ma.CompareTo(mb);
        if (c != 0) return c;
        c = mia.CompareTo(mib);
        if (c != 0) return c;
        return pa.CompareTo(pb);
    }
}

using System.Text.RegularExpressions;

namespace NothingVpn.Domain.Updates;

public static class SemanticVersionPolicy
{
    private static readonly Regex Triple = new(
        @"^(?:v|V)?(?<a>\d+)\.(?<b>\d+)(?:\.(?<c>\d+))?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? input, out int major, out int minor, out int patch)
    {
        major = minor = patch = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;
        var match = Triple.Match(input.Trim());
        if (!match.Success)
            return false;
        major = int.Parse(match.Groups["a"].Value, System.Globalization.CultureInfo.InvariantCulture);
        minor = int.Parse(match.Groups["b"].Value, System.Globalization.CultureInfo.InvariantCulture);
        patch = match.Groups["c"].Success
            ? int.Parse(match.Groups["c"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : 0;
        return true;
    }

    public static string Normalize(string? input) =>
        TryParse(input, out var major, out var minor, out var patch)
            ? FormattableString.Invariant($"{major}.{minor}.{patch}")
            : string.Empty;

    public static string ToGitTag(string version)
    {
        var normalized = Normalize(version);
        return normalized.Length == 0 ? string.Empty : $"v{normalized}";
    }

    public static int Compare(string? left, string? right)
    {
        var leftValid = TryParse(left, out var leftMajor, out var leftMinor, out var leftPatch);
        var rightValid = TryParse(right, out var rightMajor, out var rightMinor, out var rightPatch);
        if (!leftValid && !rightValid) return 0;
        if (!leftValid) return -1;
        if (!rightValid) return 1;
        var result = leftMajor.CompareTo(rightMajor);
        if (result != 0) return result;
        result = leftMinor.CompareTo(rightMinor);
        return result != 0 ? result : leftPatch.CompareTo(rightPatch);
    }
}

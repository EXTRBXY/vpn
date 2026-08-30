using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NothingVpn.Infrastructure.TunApps;

public sealed class RegistryUninstallAppsProvider : IInstalledAppsProvider
{
    private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

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
                    ReadUninstallKey(hive, view, seenPaths, result, cancellationToken);
                }
            }

            return result;
        }, cancellationToken);
    }

    private static void ReadUninstallKey(
        RegistryHive hive,
        RegistryView view,
        HashSet<string> seenPaths,
        List<AppCandidate> result,
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallSubKey, false);
            if (uninstall is null)
                return;

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var appKey = uninstall.OpenSubKey(subKeyName, false);
                if (appKey is null)
                    continue;

                var displayName = (appKey.GetValue("DisplayName") as string ?? string.Empty).Trim();
                if (displayName.Length == 0)
                    continue;

                var productName = (appKey.GetValue("ProductName") as string ?? string.Empty).Trim();
                var exePath = ExtractExePath(appKey, displayName, productName);
                if (!TunAppPathPolicy.TryNormalizeExePath(exePath, out var normalizedPath))
                    continue;
                if (!seenPaths.Add(normalizedPath))
                    continue;

                result.Add(new AppCandidate(displayName, normalizedPath, AppCandidateSource.Installed));
            }
        }
        catch
        {
            // Ignore inaccessible registry views/hives on this host.
        }
    }

    private static string? ExtractExePath(RegistryKey appKey, string displayName, string productName)
    {
               var displayIcon = appKey.GetValue("DisplayIcon") as string;
        var fromIcon = TryParseExeFromRaw(displayIcon);
        if (fromIcon is not null && !TunAppPathPolicy.IsLikelyUninstallerExe(fromIcon))
            return fromIcon;

        var fromUninstall = TryExeFromUninstallStrings(appKey);
        if (fromUninstall is not null)
            return fromUninstall;

        var installLocation = (appKey.GetValue("InstallLocation") as string ?? string.Empty).Trim().Trim('"');
        if (installLocation.Length != 0 && Directory.Exists(installLocation))
        {
            var picked = PickBestExeInDirectory(installLocation, displayName, productName);
            if (picked is not null)
                return picked;
        }

        return null;
    }

    private static string? TryExeFromUninstallStrings(RegistryKey appKey)
    {
        foreach (var valueName in new[] { "QuietUninstallString", "UninstallString" })
        {
            var raw = appKey.GetValue(valueName) as string;
            var path = TryParseFirstExeFromCommandLine(raw);
            if (path is null)
                continue;
            if (TunAppPathPolicy.IsLikelyUninstallerExe(path))
                continue;
            return path;
        }

        return null;
    }

    private static string? TryParseFirstExeFromCommandLine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim();
        if (text.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
            return null;

        var quoted = Regex.Match(text, "^\\s*\"([^\"]+\\.exe)\"\\s*", RegexOptions.IgnoreCase);
        if (quoted.Success)
        {
            var candidate = quoted.Groups[1].Value.Trim();
            if (candidate.Length > 0)
                return candidate;
        }

        foreach (Match m in Regex.Matches(text, @"([A-Za-z]:[^""\s]+\.exe)", RegexOptions.IgnoreCase))
        {
            var candidate = m.Groups[1].Value.Trim();
            if (candidate.Length > 0)
                return candidate;
        }

        return null;
    }

    private static string? PickBestExeInDirectory(string directory, string displayName, string productName)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return null;
        }

        if (files.Length == 0)
            return null;
        if (files.Length == 1)
            return TunAppPathPolicy.IsLikelyUninstallerExe(files[0]) ? null : files[0];

        var filtered = files.Where(f => !TunAppPathPolicy.IsLikelyUninstallerExe(f)).ToArray();
        if (filtered.Length == 0)
            filtered = files;

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TokenizeForMatch(displayName))
            tokens.Add(t);
        foreach (var t in TokenizeForMatch(productName))
            tokens.Add(t);

        int Score(string path)
        {
            var baseName = Path.GetFileNameWithoutExtension(path);
            var score = 0;
            foreach (var token in tokens)
            {
                if (token.Length < 3)
                    continue;
                if (baseName.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 10;
            }

            return score;
        }

        return filtered
            .OrderByDescending(Score)
            .ThenBy(p => p.Length)
            .First();
    }

    private static IEnumerable<string> TokenizeForMatch(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (var part in Regex.Split(text.Trim(), @"[^\p{L}\p{Nd}]+"))
        {
            if (part.Length >= 3)
                yield return part;
        }
    }

    private static string? TryParseExeFromRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim();

        var commaIndex = text.LastIndexOf(',');
        if (commaIndex > 0)
        {
            var suffix = text[(commaIndex + 1)..].Trim();
            if (int.TryParse(suffix, out _))
                text = text[..commaIndex];
        }

        var match = Regex.Match(text, "\"([^\"]+\\.exe)\"|([^\\s]+\\.exe)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        var captured = match.Groups[1].Success
            ? match.Groups[1].Value
            : match.Groups[2].Value;

        var candidate = captured.Trim().Trim('"');
        return candidate.Length == 0 ? null : candidate;
    }
}

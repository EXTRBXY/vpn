namespace NothingVpn.Infrastructure.TunApps;

internal sealed class TunAppsSelectionService
{
    private readonly IInstalledAppsProvider _installedAppsProvider;
    private readonly IRunningAppsProvider _runningAppsProvider;

    public TunAppsSelectionService(IInstalledAppsProvider installedAppsProvider, IRunningAppsProvider runningAppsProvider)
    {
        _installedAppsProvider = installedAppsProvider;
        _runningAppsProvider = runningAppsProvider;
    }

    public async Task<IReadOnlyList<AppCandidate>> GetInstalledCandidatesAsync(
        IEnumerable<string>? alreadySelectedPaths,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(
            TunAppPathPolicy.NormalizeDistinctPaths(alreadySelectedPaths),
            StringComparer.OrdinalIgnoreCase);

        var raw = await _installedAppsProvider.GetInstalledAppsAsync(cancellationToken);
        return NormalizeCandidates(raw, existing);
    }

    public async Task<IReadOnlyList<AppCandidate>> GetRunningCandidatesAsync(
        IEnumerable<string>? alreadySelectedPaths,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(
            TunAppPathPolicy.NormalizeDistinctPaths(alreadySelectedPaths),
            StringComparer.OrdinalIgnoreCase);

        var raw = await _runningAppsProvider.GetRunningAppsAsync(cancellationToken);
        return NormalizeCandidates(raw, existing);
    }

    private static IReadOnlyList<AppCandidate> NormalizeCandidates(
        IEnumerable<AppCandidate> candidates,
        HashSet<string> existingPaths)
    {
        var byPath = new Dictionary<string, AppCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (!TunAppPathPolicy.TryNormalizeExePath(candidate.ExePath, out var normalizedPath))
                continue;
            if (existingPaths.Contains(normalizedPath))
                continue;
            if (byPath.ContainsKey(normalizedPath))
                continue;

            var displayName = string.IsNullOrWhiteSpace(candidate.DisplayName)
                ? Path.GetFileNameWithoutExtension(normalizedPath)
                : candidate.DisplayName.Trim();

            byPath[normalizedPath] = new AppCandidate(displayName, normalizedPath, candidate.Source);
        }

        return byPath.Values
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.ExePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

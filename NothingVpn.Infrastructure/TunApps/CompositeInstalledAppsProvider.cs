namespace NothingVpn.Infrastructure.TunApps;

internal sealed class CompositeInstalledAppsProvider : IInstalledAppsProvider
{
    private readonly IReadOnlyList<IInstalledAppsProvider> _providers;

    public CompositeInstalledAppsProvider(params IInstalledAppsProvider[] providers)
    {
        _providers = providers;
    }

    public async Task<IReadOnlyList<AppCandidate>> GetInstalledAppsAsync(CancellationToken cancellationToken)
    {
        var tasks = _providers.Select(p => p.GetInstalledAppsAsync(cancellationToken)).ToArray();
        var batches = await Task.WhenAll(tasks);

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<AppCandidate>();

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var candidate in batch)
            {
                if (!TunAppPathPolicy.TryNormalizeExePath(candidate.ExePath, out var normalizedPath))
                    continue;
                if (!seenPaths.Add(normalizedPath))
                    continue;

                var displayName = string.IsNullOrWhiteSpace(candidate.DisplayName)
                    ? Path.GetFileNameWithoutExtension(normalizedPath)
                    : candidate.DisplayName.Trim();

                merged.Add(new AppCandidate(displayName, normalizedPath, AppCandidateSource.Installed));
            }
        }

        return merged;
    }
}

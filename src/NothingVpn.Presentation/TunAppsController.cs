using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;

namespace NothingVpn.Presentation;

public sealed class TunAppsController : ITunAppsController
{
    private readonly IPathPolicyPort _pathPolicy;
    private readonly ISettingsService _settingsService;

    public TunAppsController(IPathPolicyPort pathPolicy, ISettingsService settingsService)
    {
        _pathPolicy = pathPolicy;
        _settingsService = settingsService;
    }

    public IReadOnlyList<string> Normalize(IEnumerable<string>? paths) =>
        _pathPolicy.NormalizeDistinctExePaths(paths);

    public bool TryNormalize(string? path, out string normalizedPath) =>
        _pathPolicy.TryNormalizeExePath(path, out normalizedPath);

    public IReadOnlyList<string> Save(AppStateModel state, IEnumerable<string>? paths)
    {
        var normalized = Normalize(paths);
        state.TunAppProcessPaths = normalized.ToList();
        _settingsService.SaveState(state);
        return normalized;
    }

    public IReadOnlyList<string> AddAndSave(
        AppStateModel state,
        IEnumerable<string>? currentPaths,
        IEnumerable<string>? addedPaths) =>
        Save(state, (currentPaths ?? Array.Empty<string>()).Concat(addedPaths ?? Array.Empty<string>()));

    public IReadOnlyList<string> RemoveAndSave(
        AppStateModel state,
        IEnumerable<string>? currentPaths,
        string? removedPath)
    {
        var normalizedRemoved = TryNormalize(removedPath, out var value) ? value : removedPath?.Trim();
        return Save(state, (currentPaths ?? Array.Empty<string>()).Where(path =>
            !string.Equals(path, normalizedRemoved, StringComparison.OrdinalIgnoreCase)));
    }
}

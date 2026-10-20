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
        _settingsService.UpdateState(current => current.TunAppProcessPaths = normalized.ToList());
        state.TunAppProcessPaths = normalized.ToList();
        return normalized;
    }
}

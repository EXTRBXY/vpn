using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public interface ITunAppsController
{
    IReadOnlyList<string> Normalize(IEnumerable<string>? paths);
    bool TryNormalize(string? path, out string normalizedPath);
    IReadOnlyList<string> Save(AppStateModel state, IEnumerable<string>? paths);
    IReadOnlyList<string> AddAndSave(AppStateModel state, IEnumerable<string>? currentPaths, IEnumerable<string>? addedPaths);
    IReadOnlyList<string> RemoveAndSave(AppStateModel state, IEnumerable<string>? currentPaths, string? removedPath);
}

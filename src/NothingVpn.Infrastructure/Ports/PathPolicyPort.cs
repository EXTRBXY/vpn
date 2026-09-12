using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.TunApps;

namespace NothingVpn.Infrastructure.Ports;

public sealed class PathPolicyPort : IPathPolicyPort
{
    public IReadOnlyList<string> NormalizeDistinctExePaths(IEnumerable<string>? paths)
        // Saved routing choices must survive application updates, unmounted drives,
        // and temporarily inaccessible executables. Discovery checks existence separately.
        => TunAppPathPolicy.NormalizeDistinctPaths(paths, requireExistingFile: false);

    public bool TryNormalizeExePath(string? rawPath, out string normalizedPath)
        => TunAppPathPolicy.TryNormalizeExePath(rawPath, out normalizedPath, requireExistingFile: false);
}


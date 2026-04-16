using NothingVpn.Application.Ports;
using NothingVpn.Tray.Internal.TunApps;

namespace NothingVpn.Infrastructure.Ports;

public sealed class PathPolicyPort : IPathPolicyPort
{
    public IReadOnlyList<string> NormalizeDistinctExePaths(IEnumerable<string>? paths)
        => TunAppPathPolicy.NormalizeDistinctPaths(paths);

    public bool TryNormalizeExePath(string? rawPath, out string normalizedPath)
        => TunAppPathPolicy.TryNormalizeExePath(rawPath, out normalizedPath);
}


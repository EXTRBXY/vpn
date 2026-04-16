namespace NothingVpn.Application.Ports;

public interface IPathPolicyPort
{
    IReadOnlyList<string> NormalizeDistinctExePaths(IEnumerable<string>? paths);
    bool TryNormalizeExePath(string? rawPath, out string normalizedPath);
}


using System.Diagnostics;

namespace NothingVpn.Infrastructure.TunApps;

public sealed class RunningProcessesProvider : IRunningAppsProvider
{
    public Task<IReadOnlyList<AppCandidate>> GetRunningAppsAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<AppCandidate>>(() =>
        {
            var result = new List<AppCandidate>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentSessionId = Process.GetCurrentProcess().SessionId;

            foreach (var process in Process.GetProcesses())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (process)
                {
                    // For MVP: current interactive user scope by session id.
                    if (!IsInSession(process, currentSessionId))
                        continue;

                    var exePath = TryGetMainModulePath(process);
                    if (!TunAppPathPolicy.TryNormalizeExePath(exePath, out var normalizedPath))
                        continue;
                    if (TunAppPathPolicy.IsLikelyUninstallerExe(normalizedPath))
                        continue;
                    if (!seenPaths.Add(normalizedPath))
                        continue;

                    var displayName = TryGetDisplayName(process, normalizedPath);
                    result.Add(new AppCandidate(displayName, normalizedPath, AppCandidateSource.Running));
                }
            }

            return result;
        }, cancellationToken);
    }

    private static bool IsInSession(Process process, int sessionId)
    {
        try { return process.SessionId == sessionId; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static string? TryGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string TryGetDisplayName(Process process, string path)
    {
        try
        {
            var n = process.ProcessName?.Trim();
            if (!string.IsNullOrWhiteSpace(n))
                return n;
        }
        catch
        {
            // Ignore process metadata failures.
        }

        return Path.GetFileNameWithoutExtension(path);
    }
}

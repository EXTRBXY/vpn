namespace NothingVpn.Infrastructure.TunApps;

public enum AppCandidateSource
{
    Installed,
    Running
}

public sealed record AppCandidate(string DisplayName, string ExePath, AppCandidateSource Source);

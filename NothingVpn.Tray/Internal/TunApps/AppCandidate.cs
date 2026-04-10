namespace NothingVpn.Tray.Internal.TunApps;

internal enum AppCandidateSource
{
    Installed,
    Running
}

internal sealed record AppCandidate(string DisplayName, string ExePath, AppCandidateSource Source);

namespace NothingVpn.Application.Models;

public enum AppCandidateSource
{
    Installed,
    Running
}

/// <summary>Приложение, которое можно добавить в режим TUN по приложениям.</summary>
public sealed record AppCandidate(string DisplayName, string ExePath, AppCandidateSource Source);

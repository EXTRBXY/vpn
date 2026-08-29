using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public sealed record AppUpdateCheckResult(
    bool Succeeded,
    AppReleaseModel? AvailableRelease,
    string? Error = null);

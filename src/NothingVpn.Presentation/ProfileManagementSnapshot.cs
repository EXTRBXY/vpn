using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public sealed record ProfileManagementSnapshot(
    IReadOnlyList<VpnProfile> Profiles,
    string? ActiveProfileId,
    string? ChangedActiveProfileId);

using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public sealed record ConnectionScreenSnapshot(
    AppStateModel State,
    IReadOnlyList<VpnProfile> Profiles,
    VpnProfile? SelectedProfile);

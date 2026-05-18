using NothingVpn.Application.Services;

namespace NothingVpn.Infrastructure.Composition;

public sealed class ApplicationServiceBundle
{
    public required IProfileService ProfileService { get; init; }
    public required ISubscriptionService SubscriptionService { get; init; }
    public required ISettingsService SettingsService { get; init; }
    public required IVpnConnectionService VpnConnectionService { get; init; }
    public required IDiagnosticsService DiagnosticsService { get; init; }
    public required IAppLifecycleService AppLifecycleService { get; init; }
}


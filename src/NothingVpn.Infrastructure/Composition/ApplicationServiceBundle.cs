using NothingVpn.Application.Services;

namespace NothingVpn.Infrastructure.Composition;

public sealed class ApplicationServiceBundle
{
    public required IProfileService ProfileService { get; init; }
    public required ISubscriptionService SubscriptionService { get; init; }
    public required ISettingsService SettingsService { get; init; }
    public required IVpnConnectionService VpnConnectionService { get; init; }
    public required IDiagnosticsService DiagnosticsService { get; init; }
    public required NothingVpn.Application.Ports.IElevationPort ElevationPort { get; init; }
    public required IStorageHealthService StorageHealthService { get; init; }
    public required IRuleSetFileService RuleSetFileService { get; init; }
    public required IAppUpdateService AppUpdateService { get; init; }
    public required IInstallerUpdateService InstallerUpdateService { get; init; }
    public required IInstallerLaunchService InstallerLaunchService { get; init; }
    public required NothingVpn.Application.Ports.IPathPolicyPort PathPolicy { get; init; }
    public required NothingVpn.Application.Ports.ILogPort Log { get; init; }
    public required NothingVpn.Application.Ports.IAppDiscoveryPort AppDiscovery { get; init; }
}

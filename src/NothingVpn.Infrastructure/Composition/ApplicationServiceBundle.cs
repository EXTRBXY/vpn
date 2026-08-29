using NothingVpn.Application.Services;
using NothingVpn.Infrastructure.Diagnostics;

namespace NothingVpn.Infrastructure.Composition;

public sealed class ApplicationServiceBundle
{
    public required IProfileService ProfileService { get; init; }
    public required ISubscriptionService SubscriptionService { get; init; }
    public required ISettingsService SettingsService { get; init; }
    public required IVpnConnectionService VpnConnectionService { get; init; }
    public required IDiagnosticsService DiagnosticsService { get; init; }
    public required IAppLifecycleService AppLifecycleService { get; init; }
    public required IStorageHealthService StorageHealthService { get; init; }
    public required NothingVpn.Application.Ports.IPathPolicyPort PathPolicy { get; init; }

    /// <summary>Единый in-memory лог для UI и sing-box runner.</summary>
    public required InMemoryLogStore SharedLogStore { get; init; }
}

using NothingVpn.Application.Services;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.Runtime;
using NothingVpn.Infrastructure.Diagnostics;

namespace NothingVpn.Infrastructure.Composition;

public static class ApplicationServicesFactory
{
    public static ApplicationServiceBundle CreateDefault()
    {
        var appPaths = new AppPathsPort();
        var logStore = new InMemoryLogStore(maxBytes: 1_000_000);
        var runtime = new LegacyRuntimeContext(appPaths, logStore);
        var pathPolicy = new PathPolicyPort();
        var profileStore = new ProfileStorePort(appPaths);
        var subscriptionStore = new SubscriptionStorePort(appPaths);
        var stateStore = new StateStorePort(appPaths);
        var parser = new ProfileParserPort();
        var subscriptionFetcher = new SubscriptionHttpFetcher();
        var diagnosticsPort = new DiagnosticsPort();
        var logPort = new LogPort(runtime);
        var proxyPort = new ProxyPort();
        var elevationPort = new ElevationPort();
        var singBoxPort = new SingBoxPort(runtime);

        var profileService = new ProfileService(profileStore, parser);
        var settingsService = new SettingsService(stateStore, pathPolicy);
        var subscriptionService = new SubscriptionService(
            subscriptionStore,
            subscriptionFetcher,
            profileStore,
            parser,
            settingsService);
        var diagnosticsService = new DiagnosticsService(diagnosticsPort, logPort, stateStore);
        var appLifecycleService = new AppLifecycleService(elevationPort);
        var vpnService = new VpnConnectionService(
            profileStore,
            stateStore,
            singBoxPort,
            proxyPort,
            diagnosticsPort,
            elevationPort,
            appPaths,
            pathPolicy,
            logPort);
        vpnService.RecoverStaleRuntimeState();

        return new ApplicationServiceBundle
        {
            ProfileService = profileService,
            SubscriptionService = subscriptionService,
            SettingsService = settingsService,
            VpnConnectionService = vpnService,
            DiagnosticsService = diagnosticsService,
            AppLifecycleService = appLifecycleService,
            SharedLogStore = logStore
        };
    }
}

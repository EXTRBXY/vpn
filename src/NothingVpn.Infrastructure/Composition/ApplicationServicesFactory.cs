using NothingVpn.Application.Engine;
using NothingVpn.Application.Services;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.Runtime;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.RuleSets;
using NothingVpn.Infrastructure.ServiceChannel;
using NothingVpn.Infrastructure.TunApps;
using NothingVpn.Infrastructure.Updates;

namespace NothingVpn.Infrastructure.Composition;

public static class ApplicationServicesFactory
{
    /// <summary>
    /// Переменная окружения выбора ядра для разработки (шаг 7а): <c>service</c> — ядро в службе через канал
    /// (<see cref="PipeVpnEngine"/>). По умолчанию и при любом другом значении — ядро внутри процесса, как в релизе.
    /// </summary>
    public const string EngineVariable = "NOTHINGVPN_ENGINE";

    internal static bool UseServiceEngine(string? value) =>
        string.Equals(value?.Trim(), "service", StringComparison.OrdinalIgnoreCase);

    public static ApplicationServiceBundle CreateDefault()
    {
        var appPaths = new AppPathsPort();
        var logStore = new InMemoryLogStore(maxBytes: 1_000_000);
        var runtime = new LegacyRuntimeContext(appPaths, logStore);
        var pathPolicy = new PathPolicyPort();
        var profileStore = new ProfileStorePort(appPaths);
        var subscriptionStore = new SubscriptionStorePort(appPaths);
        var stateStore = new AppStateStore(new StateStorePort(appPaths));
        var parser = new ProfileParserPort();
        var subscriptionFetcher = new SubscriptionHttpFetcher();
        var diagnosticsPort = new DiagnosticsPort();
        var logPort = new LogPort(logStore);
        var proxyPort = new ProxyPort();
        var elevationPort = new ElevationPort();
        var storageHealthPort = new StorageHealthPort();
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
        var storageHealthService = new StorageHealthService(storageHealthPort);
        var ruleSetFileService = new RuleSetFileService(appPaths);
        var appUpdateService = new GitHubAppUpdateService();
        var installerUpdateService = new InstallerUpdateService();
        var installerLaunchService = new InstallerLaunchService();
        var engine = CreateEngine(singBoxPort, diagnosticsPort, pathPolicy, logPort, runtime);
        var vpnService = new VpnConnectionService(
            profileStore,
            stateStore,
            engine,
            proxyPort,
            elevationPort,
            ruleSetFileService,
            pathPolicy);
        stateStore.SaveMissingBuiltinRuleSets();
        vpnService.RecoverStaleRuntimeState();

        return new ApplicationServiceBundle
        {
            ProfileService = profileService,
            SubscriptionService = subscriptionService,
            SettingsService = settingsService,
            VpnConnectionService = vpnService,
            DiagnosticsService = diagnosticsService,
            AppLifecycleService = appLifecycleService,
            StorageHealthService = storageHealthService,
            RuleSetFileService = ruleSetFileService,
            AppUpdateService = appUpdateService,
            InstallerUpdateService = installerUpdateService,
            InstallerLaunchService = installerLaunchService,
            PathPolicy = pathPolicy,
            Log = logPort,
            AppDiscovery = CreateAppDiscovery(),
            SharedLogStore = logStore
        };
    }

    private static IVpnEngine CreateEngine(
        SingBoxPort singBoxPort,
        DiagnosticsPort diagnosticsPort,
        PathPolicyPort pathPolicy,
        LogPort logPort,
        LegacyRuntimeContext runtime)
    {
        if (!UseServiceEngine(Environment.GetEnvironmentVariable(EngineVariable)))
            return new SingBoxVpnEngine(singBoxPort, diagnosticsPort, pathPolicy, logPort);

        var engine = new PipeVpnEngine(logPort, runtime.Paths.RuleSetsDir);
        // Состояние службы нужно до RecoverStaleRuntimeState; недоступная служба — не ошибка запуска клиента.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Task.Run(() => engine.RefreshStatusAsync(timeout.Token)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logPort.Append(Domain.Models.LogLevel.Warn, "app", $"Служба Nothing VPN недоступна: {ex.Message}");
        }

        return engine;
    }

    private static TunAppsSelectionService CreateAppDiscovery() =>
        new(
            new CompositeInstalledAppsProvider(
                new RegistryUninstallAppsProvider(),
                new AppPathsRegistryProvider(),
                new StartMenuShortcutAppsProvider()),
            new RunningProcessesProvider());
}

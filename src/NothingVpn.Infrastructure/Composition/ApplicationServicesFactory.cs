using NothingVpn.Application.Engine;
using NothingVpn.Application.Ports;
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
    /// Переменная окружения выбора ядра. По умолчанию (шаг 7б) — служба через канал (<see cref="PipeVpnEngine"/>).
    /// <c>inprocess</c> — ядро внутри процесса интерфейса: только для разработки без установленной службы; для TUN
    /// клиент нужно самому запустить от имени администратора. Другие значения — служба: опечатка не должна молча
    /// переводить ядро в процесс интерфейса.
    /// </summary>
    public const string EngineVariable = "NOTHINGVPN_ENGINE";

    internal static bool UseInProcessEngine(string? value) =>
        string.Equals(value?.Trim(), "inprocess", StringComparison.OrdinalIgnoreCase);

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
            settingsService,
            logPort);
        var diagnosticsService = new DiagnosticsService(diagnosticsPort, stateStore);
        var storageHealthService = new StorageHealthService(storageHealthPort, logPort);
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
            pathPolicy,
            logPort);
        stateStore.SaveMissingBuiltinRuleSets();
        vpnService.RecoverStaleRuntimeState();

        return new ApplicationServiceBundle
        {
            ProfileService = profileService,
            SubscriptionService = subscriptionService,
            SettingsService = settingsService,
            VpnConnectionService = vpnService,
            DiagnosticsService = diagnosticsService,
            ElevationPort = elevationPort,
            StorageHealthService = storageHealthService,
            RuleSetFileService = ruleSetFileService,
            AppUpdateService = appUpdateService,
            InstallerUpdateService = installerUpdateService,
            InstallerLaunchService = installerLaunchService,
            PathPolicy = pathPolicy,
            Log = logPort,
            AppDiscovery = CreateAppDiscovery()
        };
    }

    private static IVpnEngine CreateEngine(
        SingBoxPort singBoxPort,
        DiagnosticsPort diagnosticsPort,
        PathPolicyPort pathPolicy,
        LogPort logPort,
        LegacyRuntimeContext runtime)
    {
        if (UseInProcessEngine(Environment.GetEnvironmentVariable(EngineVariable)))
        {
            logPort.Append(Domain.Models.LogLevel.Warn, AppLogSources.App, $"Ядро внутри процесса интерфейса ({EngineVariable}=inprocess).");
            return new SingBoxVpnEngine(singBoxPort, diagnosticsPort, pathPolicy, logPort);
        }

        var engine = new PipeVpnEngine(logPort, runtime.Paths.RuleSetsDir);
        // Состояние службы нужно до RecoverStaleRuntimeState: работающая в службе сессия (интерфейс перезапущен)
        // не застряла. Недоступная служба — не ошибка запуска: ядро считается остановленным (sing-box живёт в Job
        // Object службы), а при подключении пользователь увидит причину.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Task.Run(() => engine.RefreshStatusAsync(timeout.Token)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logPort.Append(Domain.Models.LogLevel.Warn, AppLogSources.App, $"Служба Nothing VPN недоступна: {ex.Message}");
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

using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Infrastructure.Composition;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf.Composition;

/// <summary>Действия оболочки (окно, трей, выход), которые нужны view model'ям.</summary>
internal sealed record DesktopShell(
    Action RequestExit,
    Action<Action> ExitThenLaunch,
    Action<AppReleaseModel> NotifyUpdateAvailable,
    Action<ReleaseChangelog> ShowChangelog);

/// <summary>Собранный клиент. Наружу — только типы Application/Presentation и view model'и.</summary>
internal sealed record DesktopComposition(
    MainViewModel Main,
    SubscriptionViewModel Subscriptions,
    UpdateViewModel Updates,
    IConnectionController Connection,
    ISubscriptionService SubscriptionService,
    IStorageHealthService StorageHealth,
    ILogPort Log);

/// <summary>
/// UI-уровень композиции. Единственное место WPF-клиента, которое знает об Infrastructure: сервисы создаёт
/// <see cref="ApplicationServicesFactory"/>, здесь из них собираются контроллеры Presentation и view model'и.
/// </summary>
internal static class DesktopCompositionRoot
{
    public static DesktopComposition Create(DesktopShell shell)
    {
        var services = ApplicationServicesFactory.CreateDefault();
        var connectionController = new ConnectionController(
            services.VpnConnectionService,
            services.ElevationPort);
        var screenController = new ConnectionScreenController(
            services.ProfileService,
            services.SettingsService);
        var profileController = new ProfileManagementController(
            services.ProfileService,
            services.SettingsService.GetState().ActiveProfileId);
        var subscriptionController = new SubscriptionManagementController(services.SubscriptionService);

        var subscriptionViewModel = new SubscriptionViewModel(subscriptionController);
        var profileViewModel = new ProfileViewModel(profileController, subscriptionViewModel);
        var updateViewModel = new UpdateViewModel(
            controller: new AppUpdateController(services.AppUpdateService, services.SettingsService),
            installer: services.InstallerUpdateService,
            launcher: services.InstallerLaunchService,
            settings: services.SettingsService,
            state: services.SettingsService.GetState(),
            log: services.Log,
            exitThenLaunch: shell.ExitThenLaunch,
            notifyAvailable: shell.NotifyUpdateAvailable,
            showChangelog: shell.ShowChangelog);
        var settingsViewModel = new SettingsViewModel(
            new ConnectionSettingsController(services.SettingsService),
            new TunAppsController(services.PathPolicy, services.SettingsService),
            new RuleSetManagementController(services.SettingsService),
            services.RuleSetFileService,
            services.AppDiscovery,
            services.SettingsService,
            updateViewModel);
        var mainViewModel = new MainViewModel(
            screenController,
            connectionController,
            profileViewModel,
            settingsViewModel,
            new ConnectionDiagnosticController(services.DiagnosticsService),
            services.Log,
            shell.RequestExit);

        return new DesktopComposition(
            mainViewModel,
            subscriptionViewModel,
            updateViewModel,
            connectionController,
            services.SubscriptionService,
            services.StorageHealthService,
            services.Log);
    }
}

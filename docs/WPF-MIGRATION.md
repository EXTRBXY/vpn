# WPF migration readiness

The application logic is ready to be consumed by a WPF front end. A new WPF project must reference
`NothingVpn.Presentation`, `NothingVpn.Application`, and `NothingVpn.Domain`. Platform implementations
are obtained from `NothingVpn.Infrastructure`; WPF must not place application logic in windows or controls.

## Dependency direction

```text
NothingVpn.Domain
       ↑
NothingVpn.Application
       ↑
NothingVpn.Presentation
       ↑
WPF UI

NothingVpn.Infrastructure → Application + Domain
WPF composition root → Infrastructure
```

The WPF project may reference Infrastructure only in its composition root. View models should depend on
Application services and Presentation controllers through interfaces.

## Reusable presentation API

| WPF screen or feature | Existing reusable API |
| --- | --- |
| Main connection screen | `IConnectionScreenController`, `IConnectionController`, `ConnectionViewStateFactory` |
| DNS, TUN, proxy settings | `IConnectionSettingsController`, `ConnectionSettingsDraft` |
| Profiles | `IProfileManagementController` |
| Subscriptions | `ISubscriptionManagementController` |
| TUN applications | `ITunAppsController` |
| Rule sets | `IRuleSetManagementController`, `IRuleSetFileService` |
| Connection diagnostics | `IConnectionDiagnosticController` |
| Update availability | `IAppUpdateController` |
| Installer download/cache | `IInstallerUpdateService` |
| Installer launch | `IInstallerLaunchService` |

## Recommended WPF project layout

```text
src/NothingVpn.Desktop.Wpf/
  App.xaml
  Composition/
  ViewModels/
  Views/
  Services/       # WPF-only dialogs, clipboard, navigation
  Resources/
```

Use `CommunityToolkit.Mvvm` for observable properties and commands. Keep file pickers, clipboard,
message dialogs, tray icon, and window activation behind WPF-only adapters. Do not move these concerns
into Presentation.

## Migration order

1. Create the WPF shell and composition root using `ApplicationServicesFactory.CreateDefault()`.
2. Implement the main connection view model with `IConnectionScreenController` and `IConnectionController`.
3. Add settings, profiles, subscriptions, TUN applications, and rule-set view models.
4. Add diagnostics, logs, tray behavior, and updates.
5. Run WinForms and WPF side by side against the same controllers until feature parity is confirmed.
6. Switch the packaged executable to WPF, then remove `NothingVpn.Tray` only after parity testing.

## Acceptance criteria before removing WinForms

- Proxy, full TUN, and selected-app TUN connect and disconnect correctly.
- UAC restart and stale-runtime recovery work.
- Profile and subscription CRUD and refresh behavior match WinForms.
- DNS/TUN/proxy settings persist and validate identically.
- Rule-set import, download, update, disable, and deletion work.
- Logs, diagnostics, tray commands, startup arguments, single-instance activation, and updates work.
- `build-app.cmd` and `build-installer.cmd` package the WPF executable.
- All automated tests and both release build targets pass.

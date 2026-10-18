# Тестирование

## Состав

Один проект `tests/NothingVpn.Application.Tests` (xUnit 2.5, `net8.0-windows`, `UseWPF` + `UseWindowsForms`).
Несмотря на имя, он покрывает все слои и ссылается на все проекты, включая `NothingVpn.Desktop.Wpf`.
`InternalsVisibleTo` открывает тестам internal-типы Infrastructure (`SingBoxConfigGenerator`, `VlessLinkParser`,
`DpapiJsonFile`, `AppPaths`, …).

Состояние на 18.10.2026: 277 тестов, 274 проходят, 3 desktop-теста пропускаются вне специального запуска. Сборка
Release — без предупреждений.

| Область | Файлы |
| --- | --- |
| Domain-политики | `DomainPoliciesTests`, `SemanticVersionPolicyTests`, `InstallerFilePolicyTests`, `SubscriptionBodyDecoderTests`, `SubscriptionUserinfoParserTests`, `AppCloseBehaviorTests` |
| Генерация sing-box | `SingBoxConfigGeneratorTests`, `SecurityRegressionTests` |
| Use-case сервисы | `VpnConnectionServiceTests`, `SubscriptionServiceSyncTests` |
| Presentation-контроллеры | `Connection*ControllerTests`, `ConnectionViewStateFactoryTests`, `ProfileManagementControllerTests`, `SubscriptionManagementControllerTests`, `RuleSetManagementControllerTests`, `TunAppsControllerTests`, `AppUpdateControllerTests` |
| Infrastructure | `DpapiJsonFileTests`, `ProfileParserPortTests`, `RuleSetFileServiceTests`, `SubscriptionHttpFetcherTests`, `SingBoxRunnerTests`, `ProcessJobScopeTests`, `InstallerUpdateServiceTests`, `InstallerLaunchServiceTests` |
| WPF | `SettingsViewModelTests` (реальные контроллеры, изолированное хранилище), `WpfSettingsBindingTests` (настоящие контролы на STA-потоке) |
| Характеризационные | `SingBoxConfigGoldenTests` (эталоны в `Golden/SingBox`), `StorageRoundtripTests`, `StorageFormatCompatibilityTests`, `VlessStableIdTests` |
| Desktop-only | `WindowsUpdateHandoffTests` — запускаются только через `build/Test-UpdateHandoff.ps1` |

Фейки объявлены внутри каждого тестового класса (`private sealed class Fake…`); общей библиотеки тестовых двойников
нет. `ISettingsService` фейки часто реализуют без `UpdateState` и полагаются на default-реализацию интерфейса.

## Запуск

```powershell
.\build\Build.ps1 -Target Test        # restore + build + тесты + WPF smoke-test
dotnet test NothingVpn.sln -c Release  # только тесты
dotnet test NothingVpn.sln -c Release --no-build --filter "FullyQualifiedName~SingBoxConfigGenerator"
```

TRX-результаты `Build.ps1` кладёт в `artifacts/test-results/`.

### WPF smoke-test

`Build.ps1 -Target Test` после тестов запускает `NothingVpn.Desktop.Wpf.exe --smoke-test`: приложение проходит
полный `OnStartup` (композиция, окно, трей) и выходит с кодом 0. Ошибка пишется в `wpf-smoke-error.txt` рядом с exe.

Smoke-test работает с **настоящим** каталогом данных `%LOCALAPPDATA%\NothingVpn.Tray` текущего пользователя и
вызывает `RecoverStaleRuntimeState` (может восстановить системный прокси). На машине разработчика это нормально,
но учитывайте при запуске на рабочем профиле.

### Desktop-тесты обновления

```powershell
.\build\Test-UpdateHandoff.ps1                     # обычный терминал
.\build\Test-UpdateHandoff.ps1 -RequireElevated    # терминал «от имени администратора»
```

Нужны интерактивный рабочий стол и Explorer. Включаются переменными `NOTHINGVPN_RUN_DESKTOP_TESTS=1` и
`NOTHINGVPN_EXPECT_ELEVATED`. Подробности — [UPDATE-HANDOFF.md](UPDATE-HANDOFF.md).

## Что не покрыто автоматически

- Реальный запуск sing-box и прохождение трафика (proxy, TUN, TUN по приложениям).
- UAC и takeover-перезапуск.
- Реальная установка обновления.
- `WinInetProxyController` (пишет в реестр текущего пользователя).
- Полный roundtrip всех полей через `LegacyModelMapper` и DPAPI-хранилище (риск тихой потери нового поля).
- Провайдеры поиска установленных/запущенных приложений.
- Трей, single-instance, пересылка аргументов, тема.

Для этого есть ручной чек-лист: [UI-REGRESSION-CHECKLIST.md](UI-REGRESSION-CHECKLIST.md).

## Характеризационные тесты

Фиксируют текущее поведение перед рефакторингом. Если такой тест упал, изменилось поведение, видимое пользователю,
или формат данных на диске. Это либо ошибка, либо осознанное решение, которому нужна миграция.

- `SingBoxConfigGoldenTests` — итоговый JSON sing-box целиком: каждый режим × DoH/system × detour × с rule-set и
  без, плюс профили tls+ws и grpc без TLS. Эталоны лежат в `Golden/SingBox/`. Чтобы осознанно их обновить,
  запустите тесты с `NOTHINGVPN_UPDATE_GOLDEN=1` и проверьте изменения в diff.
- `StorageRoundtripTests` — каждое поле `AppStateModel`/`VpnProfile`/`SubscriptionModel` проходит через порт,
  маппер и DPAPI-файл без потерь. Падает, если новое поле не заполнено в тесте или не проброшено через маппер.
  Также проверяет, что Application-модели и классы хранения совпадают по набору полей; при намеренном разделении
  моделей этот тест нужно переписать.
- `StorageFormatCompatibilityTests` — JSON текущей схемы (зашифрованный и legacy-открытый) читается с ожидаемыми
  значениями, значения по умолчанию для отсутствующих полей, имена полей в сохранённых файлах.
- `VlessStableIdTests` — Id профилей на эталонных ссылках.

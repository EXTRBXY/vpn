# Тестирование

## Состав

Один проект `tests/NothingVpn.Application.Tests` (xUnit 2.5, `net8.0-windows`, `UseWPF` + `UseWindowsForms`).
Несмотря на имя, он покрывает все слои и ссылается на все проекты, включая `NothingVpn.Desktop.Wpf`.
`InternalsVisibleTo` открывает тестам internal-типы Infrastructure (`SingBoxConfigGenerator`, `VlessLinkParser`,
`DpapiJsonFile`, `AppPaths`, …).

Состояние на 20.10.2026 (после шага 9): 462 теста, все проходят, пропущенных нет. На шаге 9 добавлены тесты событий
журнала и маскирования (`VpnConnectionServiceTests`, `SubscriptionServiceSyncTests`, `UpdateViewModelTests`,
`StorageHealthServiceTests`) и фильтра уровня в «Диагностике» (`WpfSettingsBindingTests`); удалены тесты методов,
которые вызывал только WinForms-клиент (`ConnectionScreenController.Save`, `MarkDownloaded`,
`MarkBuiltinFilesRemoved`, `AddAndSave`/`RemoveAndSave`). Сборка Release — без предупреждений.

| Область | Файлы |
| --- | --- |
| Domain-политики | `DomainPoliciesTests`, `SemanticVersionPolicyTests`, `InstallerFilePolicyTests`, `SubscriptionBodyDecoderTests`, `SubscriptionUserinfoParserTests`, `AppCloseBehaviorTests` |
| Генерация sing-box | `SingBoxConfigGeneratorTests`, `SecurityRegressionTests` |
| Use-case сервисы | `VpnConnectionServiceTests` (оркестрация вместе с настоящим `SingBoxVpnEngine` поверх поддельного sing-box; TUN без прав с ядром в процессе — ошибка без запуска; с поддельным ядром службы — TUN без UAC, перезапуск интерфейса при работающей в службе сессии, остановка службы), `SubscriptionServiceSyncTests`, `StorageHealthServiceTests` (события журнала клиента: подключение, подписки, хранилище — без адреса сервера, UUID, ключей и адреса подписки) |
| Ядро и его контракт | `SingBoxVpnEngineTests` (версия контракта, валидация и нормализация запроса, откат при сбое запуска и порта, выход процесса), `EngineContractTests` (JSON-roundtrip запроса и статуса, контракт v2 — rule-set содержимым в base64, конфиг из запроса = конфиг из состояния на матрице эталонов) |
| Служба и канал | `ServiceProtocolTests` (кадр с длиной и JSON, версия, пределы кадра и rule-set, обрезанные и некорректные кадры, тайм-аут тела), `ServiceChannelTests` (сервер и клиент канала в одном процессе с поддельным sing-box: connect/disconnect/статус/журнал, ошибки ядра, старая версия клиента, обрыв и переподключение, rule-set содержимым в каталог службы, конфиг службы ≠ in-process только путями rule-set, отказ проверки сервера без отправки данных, ACL канала и каталога данных, `FILE_FLAG_FIRST_PIPE_INSTANCE`, поиск sing-box службой, выбор реализации ядра) |
| Установка | `LegacyUserInstallMigrationTests` (удаление старой per-user копии без каталога данных и посторонних файлов, перенос автозапуска; каталог данных службы, созданный заранее пользователем, заменяется) — реестр подменён, файлы во временном каталоге |
| Presentation-контроллеры | `Connection*ControllerTests`, `ConnectionViewStateFactoryTests`, `ProfileManagementControllerTests`, `SubscriptionManagementControllerTests`, `RuleSetManagementControllerTests`, `TunAppsControllerTests`, `AppUpdateControllerTests` |
| Infrastructure | `DpapiJsonFileTests`, `ProfileParserPortTests`, `RuleSetFileServiceTests`, `SubscriptionHttpFetcherTests`, `SingBoxRunnerTests`, `ProcessJobScopeTests`, `InstallerUpdateServiceTests`, `InstallerLaunchServiceTests` (отказ в повышенном процессе без запуска, прямой запуск без ShellExecute с тихими аргументами, имя файла установщика, пути с метасимволами — данные, без переменных `NOTHINGVPN_*`) |
| WPF | `SettingsViewModelTests` (реальные контроллеры, изолированное хранилище; разделы настроек и одно сохранение), `UpdateViewModelTests` (в том числе порядок обновления: проверки до выхода, запуск установщика — только после выхода), `WpfSettingsBindingTests` (настоящие контролы на STA-потоке, в том числе фильтр уровня журнала в «Диагностике»), `AsyncRelayCommandTests`, `GlobalExceptionHandlerTests` (журнал с маскированием, сообщение, аварийная остановка VPN — без настоящего падения процесса) |
| Архитектура | `WpfLayerBoundaryTests` — сборка WPF не ссылается на Infrastructure вне `Composition/` (сигнатуры, IL методов, XAML) |
| Характеризационные | `SingBoxConfigGoldenTests` (эталоны в `Golden/SingBox`), `StorageRoundtripTests`, `StorageFormatCompatibilityTests`, `VlessStableIdTests` |

Фейки объявлены внутри каждого тестового класса (`private sealed class Fake…`); общей библиотеки тестовых двойников
нет. Фейки `ISettingsService` обязаны реализовать `UpdateState` (default-реализации в интерфейсе нет). Тесты гонок
состояния — `VpnConnectionServiceTests` (сохранение настроек во время подключения, защита runtime-флагов, `GetStatus`
без чтения стора, откат при сбое sing-box и WinINET, восстановление прокси после выхода процесса) и
`SettingsServiceTests`.

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
но учитывайте при запуске на рабочем профиле. Клиент при старте до 3 с ищет службу; в CI её нет — это
предупреждение в журнале, а не ошибка smoke-test.

## Что не покрыто автоматически

- Реальный запуск sing-box и прохождение трафика (proxy, TUN, TUN по приложениям).
- Установщик (`NothingVpn.iss`: регистрация, остановка и удаление службы, миграция со старой установки,
  `runasoriginaluser`), запуск службы Windows, канал от настоящей службы LocalSystem (положительный путь проверки
  сервера подменяется в тестах; отказ для сервера от имени пользователя проверяется настоящей проверкой), реальная
  установка обновления и UAC установщика в тихом режиме. Ручная проверка —
  [SERVICE-INSTALL-CHECKLIST.md](SERVICE-INSTALL-CHECKLIST.md). CI компилирует установщик только в workflow Release.
- Запуск настоящего установщика обновления из интерфейса (выход, UAC, перезапуск интерфейса) — раздел 7 чек-листа.
- `WinInetProxyController` (пишет в реестр текущего пользователя).
- Провайдеры поиска установленных/запущенных приложений.
- Трей, single-instance, пересылка аргументов, тема, таймеры подписок и обновлений.
- Подключение глобального обработчика исключений к настоящим событиям WPF/`AppDomain` (сам обработчик покрыт).

Для этого есть ручной чек-лист: [UI-REGRESSION-CHECKLIST.md](UI-REGRESSION-CHECKLIST.md).

## Характеризационные тесты

Фиксируют текущее поведение перед рефакторингом. Если такой тест упал, изменилось поведение, видимое пользователю,
или формат данных на диске. Это либо ошибка, либо осознанное решение, которому нужна миграция.

- `SingBoxConfigGoldenTests` — итоговый JSON sing-box целиком: каждый режим × DoH/system × detour × с rule-set и
  без, плюс профили tls+ws и grpc без TLS. Состояние проходит `AppStateNormalizer`, как при подключении. Эталоны лежат в `Golden/SingBox/`. Чтобы осознанно их обновить,
  запустите тесты с `NOTHINGVPN_UPDATE_GOLDEN=1` и проверьте изменения в diff.
- `StorageRoundtripTests` — каждое поле `AppStateModel`/`VpnProfile`/`SubscriptionModel` проходит через порт и
  DPAPI-файл без потерь. Падает, если новое поле не заполнено в тесте, не сохраняется или (для подписок) не проброшено
  через `SubscriptionRecord`. Также требует `[JsonPropertyName]` на каждом свойстве моделей, которые сериализуются
  напрямую, и совпадения набора полей `SubscriptionModel` и `SubscriptionRecord`.
- `StorageFormatCompatibilityTests` — JSON текущей схемы (зашифрованный и legacy-открытый) читается с ожидаемыми
  значениями, значения по умолчанию для отсутствующих полей, имена полей в сохранённых файлах.
- `VlessStableIdTests` — Id профилей на эталонных ссылках.

Рядом с ними — `SettingValueSerializationTests`: enum-поля state.json пишутся прежними строками, а неизвестные, битые
или не-строковые значения читаются как значения по умолчанию, не ломая остальной файл.

# Архитектура Nothing VPN

Nothing VPN — Windows-клиент VLESS. Приложение не реализует туннелирование само: оно генерирует JSON-конфигурацию
[sing-box](https://sing-box.sagernet.org/), запускает `sing-box.exe` дочерним процессом и управляет системным
окружением (WinINET-прокси, UAC, трей). Всё остальное — хранение профилей и подписок, политики маршрутизации,
обновления — живёт в .NET-коде.

Платформа: .NET 8, Windows x64, WPF (основной клиент) + WinForms (`NothingVpn.Tray`, оставлен как эталон до удаления).

## Проекты и слои

| Проект | TFM | Назначение |
| --- | --- | --- |
| `NothingVpn.Domain` | `net8.0` | Модели настроек и чистые политики: режимы, DNS, TUN, rule-set, подписки, версии и файлы обновлений. Зависимостей нет. |
| `NothingVpn.Application` | `net8.0` | Use-case сервисы (`VpnConnectionService`, `SettingsService`, `SubscriptionService`, …), DTO-модели и **порты** (`I*Port`) — интерфейсы к внешнему миру. |
| `NothingVpn.Presentation` | `net8.0` | UI-независимые контроллеры экранов (`*Controller`) и снимки состояния для любого фронтенда. |
| `NothingVpn.Infrastructure` | `net8.0-windows` | Реализации портов: sing-box (генератор + раннер), DPAPI-хранилище, WinINET, UAC, GitHub-обновления, поиск приложений. Содержит composition root `ApplicationServicesFactory`. |
| `NothingVpn.Desktop.Wpf` | `net8.0-windows` | Упаковываемый клиент: окна, view model'и, трей, single-instance, тема. |
| `NothingVpn.Tray` | `net8.0-windows` | Legacy WinForms-клиент. Не публикуется и не входит в установщик. |
| `tests/NothingVpn.Application.Tests` | `net8.0-windows` | Единственный тестовый проект; покрывает все слои, включая WPF. |

### Направление зависимостей

```text
Domain  ←  Application  ←  Presentation  ←  Desktop.Wpf
                ↑                                │
          Infrastructure  ←──────────────────────┘ (composition root + несколько прямых обращений, см. ниже)
```

Задуманное правило (см. `.cursor/rules`, `docs/WPF-MIGRATION.md`): UI зависит от Presentation/Application через интерфейсы,
Infrastructure используется только в composition root. **Фактически** WPF напрямую использует ещё и:

- `InMemoryLogStore` — `MainViewModel`, `DiagnosticsViewModel`;
- `TunAppsSelectionService`, `AppCandidate`, провайдеры приложений — `SettingsViewModel`, `TunAppListItem`, `App.xaml.cs`;
- `TrayStatusIconBuilder` — `App.xaml.cs`.

Для этих возможностей нет портов в Application. Это первое, что ломается при попытке заменить Infrastructure.

## Composition root

`ApplicationServicesFactory.CreateDefault()` (`src/NothingVpn.Infrastructure/Composition/ApplicationServicesFactory.cs`)
вручную создаёт все порты и сервисы и возвращает `ApplicationServiceBundle`. DI-контейнера нет. Создаётся один
`AppStateStore` на процесс — его делят `SettingsService`, `VpnConnectionService` и `DiagnosticsService`. При создании
сразу вызываются `AppStateStore.SaveMissingBuiltinRuleSets()` (явная миграция: недостающие встроенные rule-set
дописываются в `state.json`) и `VpnConnectionService.RecoverStaleRuntimeState()` — если предыдущий запуск оставил
включённый системный прокси, он восстанавливается.

Затем `App.OnStartup` (`src/NothingVpn.Desktop.Wpf/App.xaml.cs`) собирает контроллеры Presentation и view model'и,
создаёт трей, таймеры и сервер single-instance. Это второй, UI-уровень композиции; он же содержит логику трея,
таймеров и выхода.

`LegacyRuntimeContext` — внутренний контейнер, который делят `SingBoxPort` и `LogPort`: пути, общий `InMemoryLogStore`
и единственный экземпляр `SingBoxRunner`.

## Две параллельные модели данных

Важно для рефакторинга: одни и те же сущности описаны дважды.

| Application (публичный контракт) | Infrastructure (формат хранения) |
| --- | --- |
| `VpnProfile` | `Profile/VlessProfile` |
| `AppStateModel` | `Store/AppState` |
| `UserRuleSetModel` | `Store/UserRuleSet` |
| `ProxySettingsSnapshotModel` | `WinInet/WinInetProxySettingsSnapshot` |
| `SubscriptionModel` + `SubscriptionUserInfoModel` | `Store/SubscriptionRecord` (плоская) |

Перекладывание полей — вручную в `Mappers/LegacyModelMapper.cs` и `Ports/SubscriptionStorePort.cs`.
`SingBoxConfigGenerator` работает с **legacy**-типами: `SingBoxPort.WriteConfig` маппит модель обратно в `AppState`
и `VlessProfile`. Новое поле настроек, не добавленное во все места, молча теряется при сохранении.

Сверх этого Domain содержит третье представление настроек (`DnsSettings`, `TunSettings`, `ProxyConnectionSettings`),
которое `ConnectionSettingsMapper` (Application) раскладывает в плоский `AppStateModel`.

## Основные сценарии

### Подключение

`MainViewModel.ToggleConnectionAsync` → `ConnectionController.StartAsync` → `VpnConnectionService.ConnectAsync`:

1. Захват `_sessionLock` (один connect/disconnect одновременно), инкремент `_sessionGeneration`.
2. Загрузка профиля и снимка состояния из `AppStateStore`. Снимок нужен только для генерации конфига и обратно
   целиком не сохраняется — настройки, изменённые за время подключения, не теряются.
3. Нормализация режима и путей приложений; `ConnectionPolicy.EnsureTunAppsHasTargets`; `RuleSetPolicyValidator.ValidateEnabled`
   (проверяет и наличие `.srs` на диске).
4. TUN без прав администратора → `ConnectResult { RequiresElevation, ElevationArgs }` (см. «Повышение прав»).
5. Если sing-box уже работает — полный disconnect.
6. `ISingBoxPort.WriteConfig` → `configs/{profileId}.{mode}.json`; `ValidateConfig` (`sing-box check`); `Start`;
   ожидание `IsRunning` до 3 с.
7. Режим прокси: проверка, что `127.0.0.1:{LocalMixedPort}` слушает (2 с); снимок текущих настроек WinINET;
   включение системного прокси; сразу же `AppStateStore.Update` записывает `ProxyWasEnabledByUs = true` и снимок.
8. Любая ошибка → `RollbackFailedConnect` (stop, удаление конфига; если прокси включила эта попытка — восстановление
   и сброс флагов).
9. Если за время подключения началась другая сессия (generation изменился) — откат и `OperationCanceledException`.
10. В состоянии меняется только `ActiveProfileId`; режим и профиль запоминаются как текущая сессия в памяти (их отдаёт
    `GetStatus`), поднимается `ConnectionStateChanged(true)`.

`GetStatus()` / `ConnectionController.IsRunning` диск не читают: процесс — `ISingBoxPort.IsRunning`, режим и профиль —
сессия в памяти (без сессии — `proxy` и `null`).

`ConnectionController.StartAsync` при любом исключении дополнительно вызывает `DisconnectAsync`.

### Отключение и восстановление

`DisconnectCoreAsync`: `SingBoxRunner.Stop()` (kill всего дерева процессов, ожидание 2 с) → восстановление WinINET
по снимку, если прокси включали мы → одним `AppStateStore.Update` сброс флагов и `ActiveProfileId = null` → удаление
конфига → `ConnectionStateChanged(false)`.

`RecoverStaleRuntimeState` вызывается при старте и по событию `ProcessExited` раннера (sing-box упал сам). Если
`_sessionLock` занят — ничего не делает (идёт connect/disconnect).

`SingBoxRunner` привязывает процесс к Job Object с `KILL_ON_JOB_CLOSE` (`Windows/ProcessJobScope.cs`): при падении
клиента sing-box завершается вместе с ним.

### Повышение прав (TUN)

TUN требует администратора. Клиент не повышает себя «на месте»:

1. `VpnConnectionService` возвращает `ElevationArgs = --takeover --start --mode {mode} --profile "{id}"`.
2. `ConnectionController` вызывает `Elevation.RestartElevated` (`runas`). Отмена UAC → исключение.
3. `ConnectionStartOutcome.ExitCurrentProcess = true` → `MainViewModel` вызывает `App.RequestExit`, текущий процесс
   останавливает VPN и выходит, освобождая мьютекс.
4. Новый процесс с `--takeover` до 20 с ждёт мьютекс (`App.WaitForPrimaryTakeover`), затем `ApplyStartupAsync`
   выбирает режим/профиль и подключается.

После этого клиент работает повышенным до выхода. Обновление из повышенного клиента — отдельная история, см.
[UPDATE-HANDOFF.md](UPDATE-HANDOFF.md).

### Single instance и аргументы командной строки

- Мьютекс `Global\NothingVpn.Desktop.Wpf`, именованный канал `NothingVpn.Desktop.Wpf.pipe` (ACL — только текущий
  пользователь). Второй экземпляр пересылает аргументы построчно и выходит; первый показывает окно и применяет
  `StartupOptions`.
- Аргументы: `--start`, `--mode <proxy|tun|tun_apps>`, `--profile <id>` (также `--key=value`), `--takeover`,
  `--smoke-test` (поднять UI и выйти с кодом 0; ошибка пишется в `wpf-smoke-error.txt` рядом с exe).
- WinForms-клиент использует свой мьютекс `Global\NothingVpn.Tray`; установщик закрывает оба (`AppMutex` в `.iss`).

### Подписки

`SubscriptionService.RefreshAsync`: HTTPS-загрузка (`SubscriptionHttpFetcher`, без системного прокси, ≤ 4 МиБ, 90 с) →
`SubscriptionBodyDecoder` (plain или base64) → `SubscriptionLinkExtractor` (только `vless://`, остальные схемы
считаются пропущенными) → парсинг каждой ссылки → `SubscriptionHeadersParser` (`Subscription-Userinfo`,
`Profile-Update-Interval`, `Profile-Title`) → `IProfileStorePort.SyncForSubscription` (добавить/обновить/удалить
профили этой подписки по стабильному Id) → сброс `ActiveProfileId`, если активный профиль исчез.

Ошибка сохраняется в `LastError` (≤ 200 символов). Пустой результат (ни одного VLESS) считается ошибкой и **не**
удаляет существующие профили.

Автообновление: `App` проверяет подписки через 5 с после старта и затем каждые 15 минут (`RefreshAllDueAsync`,
только `Enabled` и просроченные по `UpdateIntervalHours`).

### Обновления приложения

`UpdateViewModel` + `AppUpdateController`: при старте чистится кэш установщиков, записывается установленная версия,
проверка `GET api.github.com/repos/EXTRBXY/vpn/releases/latest` раз в ~23,5 ч (таймер каждые 30 мин). Новая версия
показывается balloon-уведомлением трея один раз на тег. Установка: загрузка `NothingVpnSetup.exe` в
`%TEMP%\NothingVpn\NothingVpnSetup-x.y.z.exe` (allowlist хостов GitHub, проверка финального URL после редиректов) →
`InstallerLaunchService.ScheduleAfterApplicationExits` → выход клиента → установщик запускается тихо
(`/SILENT /SP- /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART`). Подробности и граница безопасности — в
[UPDATE-HANDOFF.md](UPDATE-HANDOFF.md).

### Настройки

Все чтения и записи `state.json` идут через общий `AppStateStore` (`Application/Services/AppStateStore.cs`):
`Update(Action<AppStateModel>)` — load → изменить → save под одним локом процесса. Записи целого объекта, полученного
раньше, нет: каждый писатель меняет только свои поля в свежей копии.

`SettingsService` — единственная точка, через которую UI меняет `state.json`:

- `UpdateState(Action<AppStateModel>)` — `AppStateStore.Update` + нормализация. Runtime-флаги подключения
  (`ConnectionRuntimeState`: `ProxyWasEnabledByUs`, `PreviousProxySettings`) переносятся из свежей копии, поэтому
  сохранение настроек их изменить не может. Их пишет только `VpnConnectionService`.
- Нормализация всегда приводит режим, DNS detour (`DnsDetourPolicy`), `CloseBehavior`, пути TUN-приложений, DNS/TUN/
  proxy-настройки к допустимым значениям.
- `ActiveProfileId` — общее поле: его выбирает UI (`SelectProfile`), выставляет/сбрасывает подключение и сбрасывает
  синхронизация подписок; каждый пишет только его.
- `ConnectionScreenController.Save(snapshot)` (нужен WinForms-клиенту) переносит из снимка только редактируемые
  настройки; выбранный профиль, порт, метаданные обновлений и runtime-флаги берутся из актуального состояния.

`SettingsViewModel` редактирует черновик и сохраняет всё одной операцией через `ConnectionSettingsController.Save`
(валидация → `UpdateState` с merge только редактируемых полей). Файлы rule-set, помеченные на удаление, удаляются
только после успешного сохранения.

## Потоки и асинхронность

- Сервисы Application не привязаны к UI-потоку (`ConfigureAwait(false)`). События `ConnectionStateChanged` могут
  прийти из пула; `MainViewModel` маршалит их через `Dispatcher.Invoke`.
- Синхронные операции с диском и DPAPI (`AppStateStore.Load/Update`) вызываются в том числе из UI-потока; частый
  опрос статуса (`IsRunning`) диск не трогает.
- `SingBoxRunner.ValidateConfig` синхронно ждёт `sing-box check` (`GetAwaiter().GetResult()`), тайм-аут 2 с.
- Таймеры WPF (`DispatcherTimer`): тема — 1 с, обновление журнала в диагностике — 1 с, проверка обновлений — 30 мин,
  подписки — 15 мин.

## Логирование

`InMemoryLogStore` — кольцевой буфер ≈ 1 МБ, общий для sing-box и UI. Каждая запись проходит `LogRedactor`
(UUID, `vless://`, путь `/sub/…`, `pbk`/`sid`/`uuid`, токены, `Authorization: Bearer`). Уровни — целые 0..6
(TRACE…PANIC). `SingBoxRunner` разбирает уровень из строки sing-box и понижает до WARN типичные «connection closed».
Буфер очищается при каждом старте sing-box. На диск журнал пишется только по явному «Сохранить» в диагностике.

WPF-клиент сам ничего в журнал не пишет — там только вывод sing-box. (В WinForms был `AppLogger`.)

## Пути и внешние зависимости во время работы

| Что | Где |
| --- | --- |
| Данные пользователя | `%LOCALAPPDATA%\NothingVpn.Tray\` (имя историческое, менять нельзя без миграции) |
| Установка | `%LOCALAPPDATA%\Programs\NothingVpn\` (per-user, без прав администратора) |
| `sing-box.exe`, `wintun.dll` | рядом с exe; поиск также в `bin\`, до 5 родительских каталогов и в каталоге установки; предпочитается каталог, где есть `wintun.dll` |
| Кэш установщика | `%TEMP%\NothingVpn\` |
| Автозапуск | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NothingVpn` (задача установщика) |

Подробно о файлах данных — [STORAGE.md](STORAGE.md).

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
| `NothingVpn.Desktop.Wpf` | `net8.0-windows` | Упаковываемый клиент: окна, view model'и, трей, single-instance, тема, обработка исключений. UI-композиция — `Composition/DesktopCompositionRoot`. |
| `NothingVpn.Tray` | `net8.0-windows` | Legacy WinForms-клиент. Не публикуется и не входит в установщик. |
| `tests/NothingVpn.Application.Tests` | `net8.0-windows` | Единственный тестовый проект; покрывает все слои, включая WPF. |

### Направление зависимостей

```text
Domain  ←  Application  ←  Presentation  ←  Desktop.Wpf
                ↑                                │
          Infrastructure  ←──────────────────────┘ (только Composition/DesktopCompositionRoot)
```

UI зависит от Presentation/Application через интерфейсы; Infrastructure WPF-клиент видит только в composition root
(шаг 5 рефакторинга, REFACTORING.md 3.5). Журнал view model'и читают через порт `ILogPort`, поиск приложений для
TUN — через `IAppDiscoveryPort` (`AppCandidate` — модель Application). Иконка трея — UI-код и живёт в WPF
(`Platform/TrayStatusIconBuilder`; у WinForms-клиента своя копия). Правило проверяет `WpfLayerBoundaryTests`: анализ
сигнатур и IL всех типов сборки WPF (включая лямбды и async-методы) и текстов XAML.

WinForms-клиент (`NothingVpn.Tray`) по-прежнему обращается к Infrastructure напрямую; он уходит на шаге 9.

## Composition root

`ApplicationServicesFactory.CreateDefault()` (`src/NothingVpn.Infrastructure/Composition/ApplicationServicesFactory.cs`)
вручную создаёт все порты и сервисы и возвращает `ApplicationServiceBundle`. DI-контейнера нет. Создаётся один
`AppStateStore` на процесс — его делят `SettingsService`, `VpnConnectionService` и `DiagnosticsService`. При создании
сразу вызываются `AppStateStore.SaveMissingBuiltinRuleSets()` (явная миграция: недостающие встроенные rule-set
дописываются в `state.json`) и `VpnConnectionService.RecoverStaleRuntimeState()` — если предыдущий запуск оставил
включённый системный прокси, он восстанавливается.

Второй, UI-уровень композиции — `DesktopCompositionRoot.Create` (`src/NothingVpn.Desktop.Wpf/Composition/`): из
`ApplicationServiceBundle` собирает контроллеры Presentation и view model'и и отдаёт наружу `DesktopComposition` —
только типы Application/Presentation и view model'и. Действия оболочки (выход, уведомление об обновлении, окно
«Что нового») приходят в него делегатами `DesktopShell`. DI-контейнера нет и здесь.

`App` (`App.xaml.cs`) только запускает, связывает и завершает части клиента (классы — в `Platform/`):

1. `SystemThemeWatcher` — тема Windows (событие `UserPreferenceChanged` + опрос раз в секунду).
2. `SingleInstance.Acquire` — мьютекс, пересылка аргументов первому экземпляру, ожидание при `--takeover`.
3. `DesktopCompositionRoot.Create`, затем `GlobalExceptionHandler.Attach` (см. «Обработка исключений»).
4. `MainWindow` и `TrayIcon` (меню, состояние, balloon-уведомление).
5. При `--smoke-test` — выход с кодом 0. Иначе: сообщение о повреждённых файлах, `UpdateCheckScheduler`
   (проверка при старте + таймер 30 мин), `ApplyStartupAsync` (`--start`/`--mode`/`--profile`),
   `SubscriptionRefreshScheduler` (через 5 с после старта + таймер 15 мин), сервер single-instance.

Порядок шагов и интервалы те же, что были в `App.xaml.cs` до шага 5.

`LegacyRuntimeContext` — внутренний контейнер, который делят `SingBoxPort` и `LogPort`: пути, общий `InMemoryLogStore`
и единственный экземпляр `SingBoxRunner`.

## Модель данных

Одна модель на Application-уровне — она же формат хранения (шаг 3 рефакторинга, см. REFACTORING.md 3.1):

| Application-модель | Файл на диске | Как сериализуется |
| --- | --- | --- |
| `AppStateModel` (+ `UserRuleSetModel`, `ProxySettingsSnapshotModel`) | `state.json` | напрямую, имена — `[JsonPropertyName]` |
| `VpnProfile` | `profiles.json` | напрямую, имена — `[JsonPropertyName]` |
| `SubscriptionModel` + `SubscriptionUserInfoModel` | `subscriptions.json` | через плоский DTO `Store/SubscriptionRecord` |

Имена JSON-свойств зафиксированы атрибутами, поэтому переименование свойства в C# не меняет формат. Единственный
ручной маппинг — `SubscriptionRecord` ↔ `SubscriptionModel` в `Ports/SubscriptionStorePort.cs` (формат на диске
плоский, модель — вложенная). Пропуск поля ловят `StorageRoundtripTests` (roundtrip каждого поля и сверка набора полей
модели и `SubscriptionRecord`) и `StorageFormatCompatibilityTests` (замороженные имена полей).

Новое поле состояния или профиля добавляется в одно место — Application-модель (с `[JsonPropertyName]`) — плюс
заполнение в `StorageRoundtripTests`; поле подписки — ещё в `SubscriptionRecord` и оба метода `SubscriptionStorePort`.

`SingBoxConfigGenerator`, `VlessLinkParser`, `WinInetProxyController` и `BuiltinGeositeRuleSets` работают с теми же
Application-моделями; `SingBoxPort.WriteConfig` передаёт их генератору без преобразований.

Domain содержит отдельное представление групп настроек (`DnsSettings`, `TunSettings`, `ProxyConnectionSettings`) для
политик; `ConnectionSettingsMapper` (Application) раскладывает его в плоский `AppStateModel` и обратно.

### Типизированные значения настроек

Перечислимые настройки — enum'ы (шаг 4, REFACTORING.md 3.3): `ConnectionMode`, `DnsMode`, `DnsDetour`,
`RuleSetAction`, `TunStack`, `LogLevel`, `DnsPreset` (Domain/Models) и `AppCloseBehavior` (Application/Models).
`LogLevel` — общий для уровня журнала sing-box в настройках и уровня записей `InMemoryLogStore`/`ILogPort`.

Строковое представление каждого enum'а — `SettingValueMap<T>` в соответствующей политике (`ConnectionPolicy.Modes`,
`DnsPolicy.Modes`/`Detours`, `RuleSetPolicyValidator.Actions`, `TunSettingsPolicy.Stacks`,
`SingBoxLogLevelPolicy.Levels`, `AppCloseBehaviors.Values`). Карта задаёт каноническую строку (`ToValue()`), толерантный
разбор (регистр и пробелы не важны, неизвестное → значение по умолчанию) и `Normalize` для неопределённых числовых
значений. Эти же строки попадают в state.json, аргумент `--mode` (`StartupArguments.Takeover`) и конфиг sing-box.
Никогда не пишите enum в строку через `ToString()` или интерполяцию — только `ToValue()`.

Domain не содержит UI: подписи значений — `Presentation/SettingLabels`, порядок пунктов комбобоксов WinForms —
`Tray/Internal/UI/ComboIndexes`; WPF привязывает `ComboBox` к enum-значениям через `Tag="{x:Static …}"`.

### Нормализация и значения по умолчанию

- Значения по умолчанию — константы Domain-политик (`ConnectionPolicy.DefaultMode`, `DnsPolicy.Default*`,
  `TunSettingsPolicy.Default*`, `ProxyConnectionPolicy.DefaultProxyOverride`, `SingBoxLogLevelPolicy.DefaultLevel`);
  модели Application и Domain ссылаются на них.
- Нормализация состояния — одна точка, `AppStateNormalizer` (Application): режим, DNS detour, `CloseBehavior`, уровень
  журнала sing-box, пути TUN-приложений (`IPathPolicyPort`), DNS/TUN/proxy-настройки через Domain-политики. Её
  вызывают `SettingsService.UpdateState` (каждая запись) и `VpnConnectionService.ConnectAsync` (снимок перед
  генерацией конфига).
- Генератор sing-box нормализованные значения не исправляет, только переводит в конфиг и проверяет то, без чего конфиг
  некорректен (SNI для DoH, rule-set, `tun_apps` без приложений). Значения, производные от Id профиля (имя интерфейса,
  адреса TUN) и «стек по умолчанию», — `TunInterfacePolicy` (Domain).

## Основные сценарии

### Подключение

`MainViewModel.ToggleConnectionAsync` → `ConnectionController.StartAsync` → `VpnConnectionService.ConnectAsync`:

1. Захват `_sessionLock` (один connect/disconnect одновременно), инкремент `_sessionGeneration`.
2. Загрузка профиля и снимка состояния из `AppStateStore`. Снимок нужен только для генерации конфига и обратно
   целиком не сохраняется — настройки, изменённые за время подключения, не теряются.
3. Нормализация снимка (`AppStateNormalizer`); `ConnectionPolicy.EnsureTunAppsHasTargets`; `RuleSetPolicyValidator.ValidateEnabled`
   (теги, имена файлов, действия); наличие `.srs` на диске — через `IRuleSetFileService.Exists` (Domain файловую систему
   не трогает).
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
4. Новый процесс с `--takeover` до 20 с ждёт мьютекс (`SingleInstance.Acquire`), затем `ApplyStartupAsync`
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

Автообновление: `SubscriptionRefreshScheduler` проверяет подписки через 5 с после старта и затем каждые 15 минут
(`RefreshAllDueAsync`, только `Enabled` и просроченные по `UpdateIntervalHours`). Сбой всего обновления пишется в
журнал (Warn); ошибки отдельных подписок, как и раньше, — в их `LastError`.

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
- Нормализация (`AppStateNormalizer`) всегда приводит режим, DNS detour (`DnsDetourPolicy`), `CloseBehavior`, уровень
  журнала, пути TUN-приложений, DNS/TUN/proxy-настройки к допустимым значениям.
- `ActiveProfileId` — общее поле: его выбирает UI (`SelectProfile`), выставляет/сбрасывает подключение и сбрасывает
  синхронизация подписок; каждый пишет только его.
- `ConnectionScreenController.Save(snapshot)` (нужен WinForms-клиенту) переносит из снимка только редактируемые
  настройки; выбранный профиль, порт, метаданные обновлений и runtime-флаги берутся из актуального состояния.

`SettingsViewModel` редактирует черновик и сохраняет всё одной операцией через `ConnectionSettingsController.Save`
(валидация → `UpdateState` с merge только редактируемых полей). Файлы rule-set, помеченные на удаление, удаляются
только после успешного сохранения.

Черновик разбит по разделам страницы (`ViewModels/Settings/`): `DnsSettingsViewModel`, `TunSettingsViewModel`,
`TunAppsSettingsViewModel`, `RuleSetsSettingsViewModel`, `ProxySettingsViewModel`, `AppBehaviorSettingsViewModel`
(поведение окна и уровень журнала); обновления — `UpdateViewModel`. Каждый раздел загружает свои поля
(`Load(state)`) и отдаёт свою часть черновика (`ToSettings`/`BuildForSave`). Сохраняет только `SettingsViewModel.Save`:
сначала собирает все разделы (ошибка MTU — до любой записи), затем одна запись `UpdateState`, затем удаление файлов
rule-set. Сообщения разделы показывают через `SettingsViewModel` (`ISettingsFeedback`). XAML привязывает каждую
карточку к своему разделу (`DataContext="{Binding Dns}"` и т. д.).

## Потоки и асинхронность

- Сервисы Application не привязаны к UI-потоку (`ConfigureAwait(false)`). События `ConnectionStateChanged` могут
  прийти из пула; `MainViewModel` маршалит их через `Dispatcher.Invoke`.
- Синхронные операции с диском и DPAPI (`AppStateStore.Load/Update`) вызываются в том числе из UI-потока; частый
  опрос статуса (`IsRunning`) диск не трогает.
- `SingBoxRunner.ValidateConfig` синхронно ждёт `sing-box check` (`GetAwaiter().GetResult()`), тайм-аут 2 с.
- Таймеры WPF (`DispatcherTimer`): тема — 1 с (`SystemThemeWatcher`), обновление журнала в диагностике — 1 с (снимок
  берётся, только если изменилась `ILogPort.Version`), проверка обновлений — 30 мин (`UpdateCheckScheduler`),
  подписки — 15 мин (`SubscriptionRefreshScheduler`).

## Логирование

`InMemoryLogStore` — кольцевой буфер ≈ 1 МБ, общий для sing-box и UI. Каждая запись проходит `LogRedactor`
(UUID, `vless://`, путь `/sub/…`, `pbk`/`sid`/`uuid`, токены, `Authorization: Bearer`). Уровни — enum `LogLevel`
(Trace…Panic, значения 0..6). `SingBoxRunner` разбирает уровень из строки sing-box и понижает до WARN типичные «connection closed».
Буфер очищается при каждом старте sing-box. На диск журнал пишется только по явному «Сохранить» в диагностике.

Порт `ILogPort` (Application) — то, чем журнал видят сервисы и WPF: `Version` (меняется при записи и очистке; по ней
диагностика понимает, что снимок устарел), `SnapshotText(minLevel)`, `TryGetLatestMessage`, `Append` (маскирование —
внутри реализации) и `Clear`. Фильтр по уровню порт поддерживает; WPF-диагностика показывает весь буфер.

Сам WPF-клиент пишет в журнал только необработанные исключения и сбои фонового обновления подписок (источник `app`);
журнала событий приложения, как `AppLogger` в WinForms, нет (REFACTORING.md, раздел 4).

## Обработка исключений

`Platform/GlobalExceptionHandler` подключается к `Application.DispatcherUnhandledException`,
`AppDomain.CurrentDomain.UnhandledException` и `TaskScheduler.UnobservedTaskException`:

- ошибка UI-потока — запись в журнал (Error, с маскированием) и сообщение пользователю, приложение работает дальше;
  ошибка, случившаяся пока сообщение открыто, только записывается;
- `OutOfMemoryException`, `AccessViolationException`, `SEHException`, `InvalidProgramException` и завершающее процесс
  исключение `AppDomain` — фатальные: запись (Fatal) и одна попытка остановить VPN (`IConnectionController.StopAsync`
  через пул, не дольше 5 с), чтобы восстановить системный прокси. Не успел — прокси восстановит
  `RecoverStaleRuntimeState` при следующем старте, sing-box завершит Job Object;
- незамеченная ошибка задачи — запись и `SetObserved`.

`AsyncRelayCommand.Execute` — `async void` поверх `ExecuteAsync`: исключение уходит в SynchronizationContext
диспетчера и попадает в обработчик. Задачи без ожидания с UI-потока запускаются через `task.Forget()` с тем же
эффектом (вместо `_ = task`, где ошибка терялась). Исключения из меню трея (WinForms) перехватывает `TrayIcon` и
передаёт в тот же обработчик.

## Пути и внешние зависимости во время работы

| Что | Где |
| --- | --- |
| Данные пользователя | `%LOCALAPPDATA%\NothingVpn.Tray\` (имя историческое, менять нельзя без миграции) |
| Установка | `%LOCALAPPDATA%\Programs\NothingVpn\` (per-user, без прав администратора) |
| `sing-box.exe`, `wintun.dll` | рядом с exe; поиск также в `bin\`, до 5 родительских каталогов и в каталоге установки; предпочитается каталог, где есть `wintun.dll` |
| Кэш установщика | `%TEMP%\NothingVpn\` |
| Автозапуск | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NothingVpn` (задача установщика) |

Подробно о файлах данных — [STORAGE.md](STORAGE.md).

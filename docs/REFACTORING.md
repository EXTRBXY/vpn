# Подготовка к рефакторингу

Документ фиксирует состояние кода на версии 0.6.8 (октябрь 2026): что нельзя ломать, где технический долг, какие
дефекты найдены при анализе и в каком порядке разумно двигаться. Ссылки даны на файлы в `src/`.

## 1. Инварианты: внешние контракты

Нарушение любого пункта ломает существующие установки или поведение у пользователей. Меняйте только с миграцией.

| Контракт | Где задан | Почему важен |
| --- | --- | --- |
| Каталог данных `%LOCALAPPDATA%\NothingVpn.Tray` | `Infrastructure/Store/AppPaths.cs`, `build/installer/NothingVpn.iss` | там профили и настройки всех пользователей |
| Формат файлов: `NV1` + DPAPI CurrentUser + camelCase JSON | `Infrastructure/Security/DpapiJsonFile.cs` | старые файлы должны читаться |
| `[JsonPropertyName]` в `AppStateModel`, `UserRuleSetModel`, `ProxySettingsSnapshotModel`, `VpnProfile`; имена свойств `SubscriptionRecord` | `Application/Models/*`, `Infrastructure/Store/JsonSubscriptionStore.cs` | это и есть схема JSON |
| Строковые значения в состоянии: `proxy`/`tun`/`tun_apps`, `doh`/`system`, `direct`/`proxy`/`block`, `tray`/`exit`, стеки TUN | Domain-политики | хранятся в `state.json` |
| Формула Id профиля (SHA-256, 16 hex) | `Infrastructure/Profile/VlessLinkParser.cs` (`StableId`) | `activeProfileId`, синхронизация подписок |
| Хеш profileId → адреса TUN `198.18.x.y/30` и `fdfe:dcba:9876:…/126` | `Domain/Policies/TunInterfacePolicy.cs` | стабильность адресов интерфейса |
| `BuiltinId`/`Tag`/`FileName` встроенных rule-set | `Infrastructure/RuleSets/BuiltinGeositeRuleSets.cs` | сопоставление с записями в состоянии и файлами на диске |
| Аргументы `--start --mode --profile`, `--smoke-test` (`--takeover` удалён на шаге 8) | `Desktop.Wpf/Platform/StartupOptions.cs` | ярлыки и скрипты пользователей, пересылка второму экземпляру, CI smoke-test |
| Мьютекс `Global\NothingVpn.Desktop.Wpf` и канал `NothingVpn.Desktop.Wpf.pipe` | `Desktop.Wpf/App.xaml.cs` (`AppId`) | установщик (`AppMutex`), второй экземпляр |
| Имя exe `NothingVpn.Desktop.Wpf.exe` | csproj, `.iss`, `Build.ps1`, `release.yml` | ярлыки, автозапуск в `HKCU\...\Run`, обновление поверх |
| Ассет релиза `NothingVpnSetup.exe`, репозиторий `EXTRBXY/vpn` | `Infrastructure/Updates/GitHubAppUpdateService.cs`, `Domain/Updates/*` | автообновление уже установленных версий |
| Тихие аргументы установщика `/SILENT /SP- /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART` (протокол `READY`/`COMMIT` удалён на шаге 8) | `Infrastructure/Updates/InstallerLaunchService.cs` | тихое обновление поверх установленной версии |
| `AppId` Inno Setup `{A2D610D2-…}` | `.iss`, `LegacyUserInstallMigration` | обновление поверх, а не параллельная установка; по нему находится старая per-user установка |
| Служба `NothingVpn` (LocalSystem), канал `NothingVpn.Service`, каталог `%ProgramFiles%\NothingVpn`, ключ `NothingVpn.Service.exe --migrate-user-install` | `NothingVpnWindowsService`, `ServiceProtocol`, `.iss` | установщик останавливает и удаляет службу по имени; установленные версии обновляются поверх |
| Итоговый JSON sing-box | `SingBoxConfigGenerator` | поведение маршрутизации; порядок правил значим |

Рекомендация: характеризационные тесты описаны в [TESTING.md](TESTING.md#характеризационные-тесты); прогоняйте их после каждого шага.

## 2. Дефекты и риски, найденные при анализе

Это не подтверждённые баг-репорты, а места, где код допускает некорректное поведение. Перед исправлением —
воспроизвести.

### 2.1. Потерянные обновления `state.json`

`VpnConnectionService` (`Application/Services/VpnConnectionService.cs`) читает и пишет состояние через
`IStateStorePort` напрямую, минуя лок и событие `SettingsService`. `ConnectAsync` загружает состояние в начале и
сохраняет **весь объект** в конце (после запуска sing-box и ожидания порта — это секунды). Всё, что UI успел сохранить
за это время, перезаписывается. То же в `DisconnectCoreAsync`, `RecoverStaleRuntimeState`, `RollbackFailedConnect`.

Обратная сторона: Presentation местами сохраняет устаревшую копию целиком через `SaveState(state)`:
`ConnectionScreenController.Load/Save`, `RuleSetManagementController.Save/MarkDownloaded`, `TunAppsController.Save`.
Такая запись может затереть runtime-флаги `proxyWasEnabledByUs`/`previousProxySettings`, записанные при подключении,
— тогда после отключения системный прокси не восстановится.

`ConnectionSettingsController.Save` и `AppUpdateController` уже используют безопасный `UpdateState` с merge.

Направление: все записи состояния — только через `UpdateState` с изменением конкретных полей; runtime-флаги
подключения вынести в отдельное хранилище или отдельный объект.

✅ **Исправлено (шаг 2).** Один `AppStateStore` на процесс (общий лок) для `SettingsService`, `VpnConnectionService`
и `DiagnosticsService`; все записи — `Update` с изменением конкретных полей свежей копии. `ConnectAsync` больше не
сохраняет снимок: флаги прокси пишутся сразу после включения прокси, `ActiveProfileId` — в конце. `SaveState(whole)`
удалён из `ISettingsService` вместе с default-реализацией `UpdateState`; контроллеры Presentation переведены на
`UpdateState` (`ConnectionScreenController.Save` переносит только редактируемые поля). Runtime-флаги выделены в
`ConnectionRuntimeState` на уровне API: `SettingsService.UpdateState` их не меняет, формат `state.json` прежний.
Тесты: `VpnConnectionServiceTests` (сохранение во время подключения, защита флагов), `SettingsServiceTests`.

### 2.2. Чтение с диска на каждый `IsRunning`

`ConnectionController.IsRunning` → `VpnConnectionService.GetStatus()` → `IStateStorePort.Load()` → чтение файла +
DPAPI-расшифровка + (возможно) запись встроенных rule-set. WPF обращается к `IsRunning` в сеттерах, `CanExecute`,
`RefreshViewState`, обработчиках трея — синхронно на UI-потоке. Статус процесса доступен без диска
(`ISingBoxPort.IsRunning`).

✅ **Исправлено (шаг 2).** `GetStatus` берёт процесс из `ISingBoxPort.IsRunning`, а режим и профиль — из сессии
в памяти `VpnConnectionService`; стор не читается (тест `GetStatus_DoesNotReadStateStore`).

### 2.3. Запись при чтении

`StateStorePort.Load` (`Infrastructure/Ports/StateStorePort.cs`) сохраняет файл, если пришлось добавить встроенные
rule-set. Побочный эффект в операции чтения, к тому же вне лока `SettingsService`.

✅ **Исправлено (шаг 2).** `Load` добавляет встроенные наборы только в модель; на диск их дописывает явная миграция
`IStateStorePort.SaveMissingBuiltinRuleSets` при старте под локом `AppStateStore`. В характеризационном тесте
`StorageFormatCompatibilityTests` изменилась только проверка записи при чтении (раньше ожидался `.bak` после `Load`,
теперь — его отсутствие; запись проверяет новый тест миграции). Эталоны и ожидаемые значения формата не менялись.

### 2.4. Необработанные исключения в WPF

Нет обработчиков `DispatcherUnhandledException`/`TaskScheduler.UnobservedTaskException`. `AsyncRelayCommand.Execute`
— `async void` без `catch`; много `_ = SomethingAsync()` и `async void` обработчиков событий (`App.xaml.cs`,
`SettingsView.xaml.cs`, `SubscriptionsWindow.xaml.cs`). Сейчас большинство обработчиков ловят исключения сами,
но любое пропущенное исключение завершит процесс без сообщения — и с включённым системным прокси до следующего старта.

✅ **Исправлено (шаг 5).** `Platform/GlobalExceptionHandler` подписан на `DispatcherUnhandledException`,
`AppDomain.UnhandledException` и `TaskScheduler.UnobservedTaskException`. Ошибка UI-потока пишется в журнал через
`ILogPort` (с маскированием) и показывается сообщением, приложение продолжает работу; фатальная — пишется, и VPN
один раз пытается остановиться (не дольше 5 с), чтобы вернуть системный прокси. `AsyncRelayCommand.Execute` и
`async void`-обработчики отдают исключение диспетчеру, т. е. тому же обработчику; `_ = task` заменены на
`task.Forget()` с тем же эффектом; молчаливые `catch { }` при выходе и в фоновом обновлении подписок теперь пишут в
журнал. Тесты: `GlobalExceptionHandlerTests`, `AsyncRelayCommandTests`.

### 2.5. Прочее

- `SingBoxVpnEngine.DisconnectAsync` (бывший `VpnConnectionService.DisconnectCoreAsync`) по сути синхронный под `Task`; `SingBoxRunner.ValidateConfig` блокирует поток
  `GetAwaiter().GetResult()`; тайм-аут `sing-box check` — 2 с (та же константа, что для остановки), на холодном
  старте может не хватить.
- `SingBoxRunner.Stop` убивает процесс без штатной остановки — проверить очистку маршрутов TUN.
- `InstallerUpdateService` скачивает через системный прокси, остальные HTTP-клиенты — без прокси.
- `SubscriptionService.ClearActiveProfileIfMissing` меняет локальную копию `state`, которая дальше не используется.
- `TunBootstrapPolicy.ResolveDefaultDomainResolver` игнорирует параметр `useDohResolver`.
- ~~Генератор подставляет DoH-сервер `1.1.1.1` при пустом значении, тогда как значение по умолчанию везде `8.8.8.8`.~~
  Исправлено на шаге 3: подставляется `DnsPolicy.DefaultDohServer`.
- Ограничения безопасности — см. [SECURITY.md](SECURITY.md) (файлы в пользовательском каталоге, отсутствие проверки
  подписи установщика).

## 3. Технический долг

### 3.1. Дублирование моделей и маппинга

Две иерархии моделей (см. [ARCHITECTURE.md](ARCHITECTURE.md#модель-данных); до шага 3) и ручной
`LegacyModelMapper`. Добавление поля настроек требует правок в `AppState`, `AppStateModel`, двух методах маппера и,
возможно, в Domain-модели и `ConnectionSettingsMapper`. Пропуск любого места — тихая потеря данных.
`SingBoxConfigGenerator` принимает legacy `AppState`/`VlessProfile`, поэтому Application-модель маппится обратно.

Направление: генератор и стора работают с одной моделью; отдельный DTO хранения допустим, но с тестом
roundtrip-а всех полей.

✅ **Исправлено (шаг 3).** Legacy-классы `AppState`, `UserRuleSet`, `VlessProfile`, `WinInetProxySettingsSnapshot` и
`LegacyModelMapper` удалены. `state.json` и `profiles.json` сериализуют Application-модели напрямую, имена полей
зафиксированы `[JsonPropertyName]` (тест требует атрибут на каждом свойстве). Подписки хранятся через плоский DTO
`SubscriptionRecord` — единственный оставшийся маппинг, закрыт roundtrip-тестом и сверкой набора полей. Генератор,
парсер ссылок, WinINET и встроенные rule-set работают с Application-моделями. Выбран путь «модели напрямую», а не
«DTO + roundtrip»: новое поле состояния или профиля добавляется в одно место, а не в четыре.

### 3.2. Нормализация в нескольких местах

Одно и то же нормализуется в Domain-политиках, `SettingsService.NormalizeState`, `ConnectionScreenController`,
`VpnConnectionService.ConnectAsync` и повторно в `SingBoxConfigGenerator` (стек, CIDR, имя файла rule-set, пути
процессов, уровень журнала, DNS). Значения по умолчанию заданы в 3–4 классах.

✅ **Исправлено (шаг 3).** Одна точка нормализации — `AppStateNormalizer` (вызывают `SettingsService.UpdateState` и
`VpnConnectionService.ConnectAsync`); генератор ничего не нормализует. Значения по умолчанию — константы
Domain-политик; производные от Id профиля значения TUN — `TunInterfacePolicy`, уровень журнала —
`SingBoxLogLevelPolicy`. На реальных состояниях итоговый JSON не изменился (golden-тесты); отличия на входах, которые
клиент не записывает, перечислены в [SINGBOX-CONFIG.md](SINGBOX-CONFIG.md#что-генератор-делает-сам-а-что-должно-быть-в-политике).
`ConnectionScreenController` по-прежнему готовит снимок для экрана (пустые списки, detour для отображения), но при
записи полагается на нормализацию `UpdateState`.

### 3.3. Строковая типизация

Режимы, DNS-режим, detour, действия rule-set, стек, close behavior, уровни журнала — строки или магические `int`
(уровни журнала 0..6 в `ILogPort`/`InMemoryLogStore`). Сравнения разбросаны с разной чувствительностью к регистру.
При переходе на enum сохраните строковое представление в JSON (раздел 1).

✅ **Исправлено (шаг 4).** Enum'ы `ConnectionMode`, `DnsMode`, `DnsDetour`, `RuleSetAction`, `TunStack`, `LogLevel`
(и для настройки sing-box, и для `ILogPort`/`InMemoryLogStore`), `DnsPreset`, `AppCloseBehavior`. Строки — в
`SettingValueMap<T>` Domain-политик (`ToValue()`/`Parse`); в state.json, `--mode` и конфиге sing-box они прежние.
Чтение state.json толерантное (`Application/Serialization/SettingValueJsonConverter.cs`): регистр не важен, неизвестная
строка или значение другого типа → значение по умолчанию поля, а не потеря всего файла. Строками остались поля профиля
VLESS (`type`, `security`, `flow`…: открытый набор из ссылки, неизвестные значения нужно сохранять как есть) и сырой
`--mode` в `StartupOptions`/`StartupArgs` (разбирается на границе). Характеризационные тесты прошли без изменения
ожиданий; новые — `SettingValueSerializationTests`.

### 3.4. Логика представления и UI-артефакты в Domain

- В Domain русские тексты для UI и исключений: `DnsPolicy.DetourToDisplayLabel`, `TunSettingsPolicy.StackToDisplayLabel`.
- Индексы комбобоксов WinForms: `StackToComboIndex`, `ComboIndexToStack`, `DetourToComboIndex`, `ComboIndexToDetour`,
  `StateToPresetIndex`, `ApplyPreset(int)`.
- `RuleSetPolicyValidator.ValidateEnabled` обращается к файловой системе (`File.Exists`).

✅ **Исправлено (шаг 4).** Подписи — `Presentation/SettingLabels`; индексы комбобоксов WinForms —
`Tray/Internal/UI/ComboIndexes`; пресеты DoH — типизированные `DnsPolicy.DetectPreset`/`ApplyPreset(DnsPreset)`.
`ValidateEnabled` проверяет только записи (теги, имена, действия), наличие файлов `VpnConnectionService` проверяет через
`IRuleSetFileService.Exists` (вместо `IAppPathsPort`). В Domain остались русские сообщения исключений валидации —
это ошибки для пользователя, а не подписи UI.

### 3.5. Нарушение границ слоёв в WPF

WPF использует Infrastructure напрямую (лог, поиск приложений, иконка трея) — нет портов. `App.xaml.cs` совмещает
composition root, трей, таймеры, single-instance, тему и выход. `SettingsViewModel` получает 8 зависимостей и
отвечает за DNS, TUN, приложения, rule-set, прокси, журнал и поведение окна одновременно.

✅ **Исправлено (шаг 5).** Порты Application: `ILogPort` (версия, снимок с фильтром по уровню, запись с
маскированием, очистка) и `IAppDiscoveryPort` (установленные и запущенные приложения; `AppCandidate` перенесён в
Application.Models); реализации — `LogPort` и `TunAppsSelectionService`, создаёт их `ApplicationServicesFactory`.
`TrayStatusIconBuilder` — UI-код — перенесён в WPF (`Platform/`), у WinForms-клиента копия. Сборка зависимостей —
`Composition/DesktopCompositionRoot`, единственное место WPF с Infrastructure (`WpfLayerBoundaryTests` проверяет IL
всех типов и XAML); трей, таймеры подписок и обновлений, тема, single-instance и диалоги — небольшие классы в
`Platform/`, `App.xaml.cs` только запускает, связывает и завершает. `SettingsViewModel` разделён на разделы
(`ViewModels/Settings/`: DNS, TUN, приложения, rule-set, прокси, поведение окна и журнал) с одним сохранением;
гарантии UI-REGRESSION-CHECKLIST.md сохранены, `SettingsViewModelTests`/`WpfSettingsBindingTests` проверяют то же
через разделы. Порядок старта, интервалы таймеров, трей и аргументы командной строки не изменились.

### 3.6. Infrastructure зависит от WinForms

`UseWindowsForms` в `NothingVpn.Infrastructure.csproj` нужен ради `TunAppIconCache` (используется только Tray),
`Elevation.RestartElevated` (`System.Windows.Forms.Application.ExecutablePath`; можно заменить на
`Environment.ProcessPath`) и `System.Drawing` в `TrayStatusIconBuilder` (UI-код в Infrastructure).

Шаг 5: `TrayStatusIconBuilder` из Infrastructure убран (WPF и Tray держат свои копии); остальное — по-прежнему.
С шага 7а зависимость стоит дороже: служба ссылается на Infrastructure и несёт WindowsDesktop runtime (exe ~72 МБ
даже со сжатием).

### 3.7. Дублирование между WinForms и WPF

`SingleInstance` и разбор аргументов (`StartupOptions` / `StartupArgs`) существуют в двух копиях. После удаления
`NothingVpn.Tray` уходят вместе с ним, но `InternalsVisibleTo("NothingVpn.Tray")`, `AppMutex` для Tray в `.iss`
и `[InstallDelete]` для `NothingVpn.Tray.exe` нужно пересмотреть осознанно (последние два защищают обновление со
старых версий). С шага 5 в двух копиях ещё и `TrayStatusIconBuilder` (`Desktop.Wpf/Platform`, `Tray/Internal/UI`).

### 3.8. Мелочи

- `Application/Class1.cs` — пустой шаблонный класс.
- ~~`AppLifecycleService.BuildTakeoverArgs` дублирует формирование аргументов в `VpnConnectionService.ConnectAsync`.~~
  Удалено вместе с UAC-перезапуском (шаг 8).
- `DiagnosticsService.ExportLogs`, `IAppUpdateController.GetCurrentReleaseAsync`, `AppPathsModel.LogsDir`,
  `AppStateModel.DebugLogs` в WPF не используются.
- ~~Часть WPF-файлов записана «в строку» (`StartupOptions`, `ThemeManager`, `DiagnosticsViewModel`, `UpdateViewModel`,
  `DiagnosticsView`, `SettingsView.xaml`, `Theme.xaml`): плохо читаются и дают огромные диффы.~~ Исправлено на шаге 5
  для WPF-проекта (также `TunAppPickerWindow`, окна подписок, длинные строки `MainWindow`); разметка до и после
  сверена поэлементно. Остальные пункты этого списка не трогались.
- `StorageIssueRegistry` — статическая глобальная очередь.
- Каждый вызов стора — полная загрузка и расшифровка файла; кэша нет.
- Фейки в тестах продублированы по классам; общего набора тестовых двойников нет.

## 4. Разрывы функциональности WPF относительно WinForms

WinForms-клиент ещё в решении как эталон (`docs/WPF-MIGRATION.md`). В WPF отсутствуют:

- переключатель отладочного журнала и фильтр уровня в диагностике (WPF показывает весь буфер; порт `ILogPort`
  фильтр по уровню уже поддерживает);
- журнал событий самого приложения (`AppLogger`) — в WPF в журнал пишут sing-box и, с шага 5, только обработчик
  необработанных исключений и фоновое обновление подписок (через `ILogPort.Append`);
- отдельное окно прогресса загрузки обновления (в WPF — прогресс-бар в настройках).

Решение (18.10.2026): окно «Что нового» переносится в WPF обязательно (уже сделано, см. ниже). Остальные пункты не решены — вернуться к
ним до удаления `NothingVpn.Tray`.

Уже перенесено:

- окно «Что нового» после обновления: `UpdateViewModel.CheckAtStartupAsync` при `InstalledVersionTransition.Upgraded`
  берёт `IAppUpdateController.GetChangelogAsync` (ошибка загрузки → `ReleaseChangelog.LoadFailed`, не исключение)
  и показывает `Views/ReleaseChangelogWindow`. Тело релиза выводится как есть, без рендера markdown; при
  `--smoke-test` проверка обновлений не запускается, поэтому окно не появляется.

## 4а. Принятые решения

- **Безопасность** — решено (18.10.2026): привилегированная служба вместо повышения клиента
  ([ADR-0001](adr/0001-privileged-service.md)), шаги 6–8 раздела 6. Подпись обновлений отложена.
- **Каталог данных `NothingVpn.Tray`** — решено (18.10.2026): имя не меняем. Пользователь его не видит,
  а переименование требует миграции, правки установщика и ломает откат на старую версию.

## 5. Устаревшие артефакты в репозитории

- `.cursor/rules/nothing-vpn-singbox-architecture.mdc` описывает UI как WinForms.
- `build-app.cmd` выводит путь к `NothingVpn.Tray.exe`, хотя публикуется `NothingVpn.Desktop.Wpf.exe`.
- Версия `0.6.8` захардкожена в шести местах: три csproj (WPF, Tray, Service — с шага 7а), `Build.ps1` (значение по умолчанию для установщика),
  `.iss`, `release.yml` (значение по умолчанию для ручного запуска). Кандидат на `Directory.Build.props`.
- `docs/WPF-MIGRATION.md` описывает план миграции, который в основном выполнен.

## 6. Предлагаемый порядок

1. **Страховочная сетка.** ✅ Готово: характеризационные тесты генератора и хранения ([TESTING.md](TESTING.md#характеризационные-тесты)).
   Без них дальнейшие шаги не проверяемы.
2. **Модель состояния.** ✅ Готово: убрана перезапись целого состояния (2.1), runtime-флаги подключения отделены
   на уровне API (`ConnectionRuntimeState`), `IsRunning` не читает диск (2.2), запись при чтении убрана (2.3).
   Кэша состояния по-прежнему нет — чтения явные, через `AppStateStore`.
3. **Одна модель данных.** ✅ Готово: генератор и хранилища на Application-моделях, `LegacyModelMapper` удалён,
   имена JSON зафиксированы атрибутами; одна точка нормализации (`AppStateNormalizer`) и значений по умолчанию
   (константы Domain-политик) (3.1, 3.2).
4. **Типизация.** ✅ Готово: enum'ы для режимов/DNS/действий/стека/уровней журнала с прежними строками в JSON и
   толерантным чтением (3.3); Domain очищен от UI-подписей, индексов комбобоксов и файловой системы (3.4).
5. **Границы WPF.** ✅ Готово: порты для журнала и поиска приложений, composition root вынесен из `App`,
   `SettingsViewModel` разделён по разделам, глобальная обработка исключений (2.4, 3.5), WPF-файлы «в строку»
   переписаны (3.8, часть).
6. **Граница «интерфейс ↔ ядро».** ✅ Готово: порт `IVpnEngine` (`Connect`/`Disconnect`/`GetStatus`, событие
   статуса; журнал — через `ILogPort`), типизированный версионированный запрос `EngineConnectRequest` (не готовый
   JSON), реализация внутри процесса `SingBoxVpnEngine` поверх `ISingBoxPort`/`SingBoxRunner`; `VpnConnectionService`
   стал клиентской оркестрацией (state.json, WinINET, UAC, откат). Rule-set пока передаются ссылкой на файл
   каталога rule-set — передача содержимого перенесена на шаг 7. См. [ADR-0001](adr/0001-privileged-service.md),
   ARCHITECTURE.md («Граница "интерфейс ↔ ядро"»).
7. **Служба Windows.** Разбит на три части, каждая — отдельный PR (подробно — ADR-0001, «Порядок внедрения»):
   - **7а. Служба и транспорт по каналу.** ✅ Готово: проект `NothingVpn.Service` (служба Windows поверх
     `SingBoxVpnEngine`, sing-box только из своего каталога, данные в `%ProgramData%\NothingVpn` с ACL, журнал
     маскируется в службе), протокол канала (кадры с длиной и JSON с версией контракта, команды `connect`,
     `disconnect`, `getStatus`, `subscribeLog`, события статуса и журнала, пределы и тайм-ауты), ACL канала и
     `FILE_FLAG_FIRST_PIPE_INSTANCE`, проверка клиентом, что сервер — служба от LocalSystem, клиент `PipeVpnEngine`,
     контракт v2 (rule-set содержимым). Установщик не менялся; клиент по умолчанию работает с ядром внутри процесса
     (служба — только при `NOTHINGVPN_ENGINE=service`), поведение релиза прежнее. Служба публикуется рядом с
     клиентом. Итоговый JSON sing-box внутри процесса не изменился (эталоны те же); у службы отличаются только пути
     rule-set.
   - **7б. Установка.** ✅ Готово и проверено вручную 19.10.2026
     ([SERVICE-INSTALL-CHECKLIST.md](SERVICE-INSTALL-CHECKLIST.md), разделы 2–6): per-machine установщик в `C:\Program Files\NothingVpn`
     (выбор каталога отключён), регистрация, запуск, остановка при обновлении и удаление службы; старая per-user
     копия удаляется от имени исходного пользователя без запуска её деинсталлятора, каталог данных не трогается;
     автозапуск переносится на новый exe. Интерфейс по умолчанию работает через службу, TUN без UAC; ядро в процессе —
     только при `NOTHINGVPN_ENGINE=inprocess`. Перезапущенный интерфейс подхватывает работающую в службе сессию.
     Каталог данных службы, созданный заранее пользователем, служба не принимает. Код UAC-перезапуска и
     UPDATE-HANDOFF оставался до шага 8.
   - **7в. Переход.** ✅ Эмуляция пройдена 19.10.2026: установщик, запущенный как его запускает 0.6.8 (без прав,
     `UseShellExecute=false`, `/SILENT …`), показал UAC и установился; старая копия убрана, данные и настройки
     целы. **Промежуточный релиз не нужен.** Остаётся проверить настоящее обновление после публикации релиза со
     службой ([чек-лист, 7в-Б](SERVICE-INSTALL-CHECKLIST.md#б-настоящее-обновление)).
8. **Удаление повышения клиента.** ✅ Готово (19.10.2026): удалены UAC-перезапуск (`--takeover`/`runas`, ожидание
   мьютекса, `ElevationArgs`/`ExitCurrentProcess`, `IAppLifecycleService`), `WindowsUserProcessLauncher`,
   `InstallerUpdateWorker` и протокол `READY/COMMIT` вместе с desktop-тестами и `Test-UpdateHandoff.ps1`. Ядро в
   процессе без прав отклоняет TUN ошибкой. Установщик запускается напрямую после штатного выхода клиента, с отказом
   в повышенном процессе ([UPDATE-HANDOFF.md](UPDATE-HANDOFF.md)). `NothingVpn.Tray` адаптирован минимально. Ручная
   проверка обновления из интерфейса — [чек-лист, раздел 7](SERVICE-INSTALL-CHECKLIST.md#7-обновление-из-интерфейса).
9. **Удаление WinForms.** После закрытия разрывов (раздел 4): удалить `NothingVpn.Tray`, `TunAppIconCache`,
   зависимость Infrastructure от WinForms (3.6, 3.7).
10. **Сборка.** Единая версия, исправление `build-app.cmd`, актуализация `.cursor`-правил (раздел 5).

Шаги 2–5 нужны в любом случае и делают шаг 6 проще: порт ядра опирается на единую модель данных и типизированные
режимы. Шаги 6–8 меняют модель установки и разделены так, чтобы каждый можно было выпустить и проверить отдельно.

Каждый шаг проверяется `.\build\Build.ps1 -Target Test` и ручным чек-листом [UI-REGRESSION-CHECKLIST.md](UI-REGRESSION-CHECKLIST.md)
для затронутых сценариев.

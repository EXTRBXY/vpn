# Подготовка к рефакторингу

Документ фиксирует состояние кода на версии 0.6.8 (октябрь 2026): что нельзя ломать, где технический долг, какие
дефекты найдены при анализе и в каком порядке разумно двигаться. Ссылки даны на файлы в `src/`.

## 1. Инварианты: внешние контракты

Нарушение любого пункта ломает существующие установки или поведение у пользователей. Меняйте только с миграцией.

| Контракт | Где задан | Почему важен |
| --- | --- | --- |
| Каталог данных `%LOCALAPPDATA%\NothingVpn.Tray` | `Infrastructure/Store/AppPaths.cs`, `build/installer/NothingVpn.iss` | там профили и настройки всех пользователей |
| Формат файлов: `NV1` + DPAPI CurrentUser + camelCase JSON | `Infrastructure/Security/DpapiJsonFile.cs` | старые файлы должны читаться |
| Имена свойств `VlessProfile`, `AppState`, `UserRuleSet`, `SubscriptionRecord`, `WinInetProxySettingsSnapshot` | `Infrastructure/Store/*`, `Profile/VlessProfile.cs`, `WinInet/*` | это и есть схема JSON |
| Строковые значения в состоянии: `proxy`/`tun`/`tun_apps`, `doh`/`system`, `direct`/`proxy`/`block`, `tray`/`exit`, стеки TUN | Domain-политики | хранятся в `state.json` |
| Формула Id профиля (SHA-256, 16 hex) | `Infrastructure/Profile/VlessLinkParser.cs` (`StableId`) | `activeProfileId`, синхронизация подписок |
| Хеш profileId → адреса TUN `198.18.x.y/30` и `fdfe:dcba:9876:…/126` | `SingBoxConfigGenerator.NormalizeTunCidr/NormalizeTunInet6` | стабильность адресов интерфейса |
| `BuiltinId`/`Tag`/`FileName` встроенных rule-set | `Infrastructure/RuleSets/BuiltinGeositeRuleSets.cs` | сопоставление с записями в состоянии и файлами на диске |
| Аргументы `--takeover --start --mode --profile`, `--smoke-test` | `VpnConnectionService`, `AppLifecycleService`, `Desktop.Wpf/Platform/StartupOptions.cs` | перезапуск с UAC, CI smoke-test |
| Мьютекс `Global\NothingVpn.Desktop.Wpf` и канал `NothingVpn.Desktop.Wpf.pipe` | `Desktop.Wpf/App.xaml.cs` (`AppId`) | установщик (`AppMutex`), второй экземпляр |
| Имя exe `NothingVpn.Desktop.Wpf.exe` | csproj, `.iss`, `Build.ps1`, `release.yml` | ярлыки, автозапуск в `HKCU\...\Run`, обновление поверх |
| Ассет релиза `NothingVpnSetup.exe`, репозиторий `EXTRBXY/vpn` | `Infrastructure/Updates/GitHubAppUpdateService.cs`, `Domain/Updates/*` | автообновление уже установленных версий |
| Тихие аргументы установщика и протокол `READY`/`COMMIT`/`COMMITTED` | `Infrastructure/Updates/InstallerLaunchService.cs`, `InstallerUpdateWorker.cs` | обновление из повышенного клиента |
| `AppId` Inno Setup `{A2D610D2-…}` | `.iss` | обновление поверх, а не параллельная установка |
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

### 2.2. Чтение с диска на каждый `IsRunning`

`ConnectionController.IsRunning` → `VpnConnectionService.GetStatus()` → `IStateStorePort.Load()` → чтение файла +
DPAPI-расшифровка + (возможно) запись встроенных rule-set. WPF обращается к `IsRunning` в сеттерах, `CanExecute`,
`RefreshViewState`, обработчиках трея — синхронно на UI-потоке. Статус процесса доступен без диска
(`ISingBoxPort.IsRunning`).

### 2.3. Запись при чтении

`StateStorePort.Load` (`Infrastructure/Ports/StateStorePort.cs`) сохраняет файл, если пришлось добавить встроенные
rule-set. Побочный эффект в операции чтения, к тому же вне лока `SettingsService`.

### 2.4. Необработанные исключения в WPF

Нет обработчиков `DispatcherUnhandledException`/`TaskScheduler.UnobservedTaskException`. `AsyncRelayCommand.Execute`
— `async void` без `catch`; много `_ = SomethingAsync()` и `async void` обработчиков событий (`App.xaml.cs`,
`SettingsView.xaml.cs`, `SubscriptionsWindow.xaml.cs`). Сейчас большинство обработчиков ловят исключения сами,
но любое пропущенное исключение завершит процесс без сообщения — и с включённым системным прокси до следующего старта.

### 2.5. Прочее

- `VpnConnectionService.DisconnectCoreAsync` синхронный под `Task`; `SingBoxRunner.ValidateConfig` блокирует поток
  `GetAwaiter().GetResult()`; тайм-аут `sing-box check` — 2 с (та же константа, что для остановки), на холодном
  старте может не хватить.
- `SingBoxRunner.Stop` убивает процесс без штатной остановки — проверить очистку маршрутов TUN.
- `InstallerUpdateService` скачивает через системный прокси, остальные HTTP-клиенты — без прокси.
- `SubscriptionService.ClearActiveProfileIfMissing` меняет локальную копию `state`, которая дальше не используется.
- `TunBootstrapPolicy.ResolveDefaultDomainResolver` игнорирует параметр `useDohResolver`.
- Генератор подставляет DoH-сервер `1.1.1.1` при пустом значении, тогда как значение по умолчанию везде `8.8.8.8`.
- Ограничения безопасности — см. [SECURITY.md](SECURITY.md) (файлы в пользовательском каталоге, отсутствие проверки
  подписи установщика).

## 3. Технический долг

### 3.1. Дублирование моделей и маппинга

Две иерархии моделей (см. [ARCHITECTURE.md](ARCHITECTURE.md#две-параллельные-модели-данных)) и ручной
`LegacyModelMapper`. Добавление поля настроек требует правок в `AppState`, `AppStateModel`, двух методах маппера и,
возможно, в Domain-модели и `ConnectionSettingsMapper`. Пропуск любого места — тихая потеря данных.
`SingBoxConfigGenerator` принимает legacy `AppState`/`VlessProfile`, поэтому Application-модель маппится обратно.

Направление: генератор и стора работают с одной моделью; отдельный DTO хранения допустим, но с тестом
roundtrip-а всех полей.

### 3.2. Нормализация в нескольких местах

Одно и то же нормализуется в Domain-политиках, `SettingsService.NormalizeState`, `ConnectionScreenController`,
`VpnConnectionService.ConnectAsync` и повторно в `SingBoxConfigGenerator` (стек, CIDR, имя файла rule-set, пути
процессов, уровень журнала, DNS). Значения по умолчанию заданы в 3–4 классах.

### 3.3. Строковая типизация

Режимы, DNS-режим, detour, действия rule-set, стек, close behavior, уровни журнала — строки или магические `int`
(уровни журнала 0..6 в `ILogPort`/`InMemoryLogStore`). Сравнения разбросаны с разной чувствительностью к регистру.
При переходе на enum сохраните строковое представление в JSON (раздел 1).

### 3.4. Логика представления и UI-артефакты в Domain

- В Domain русские тексты для UI и исключений: `DnsPolicy.DetourToDisplayLabel`, `TunSettingsPolicy.StackToDisplayLabel`.
- Индексы комбобоксов WinForms: `StackToComboIndex`, `ComboIndexToStack`, `DetourToComboIndex`, `ComboIndexToDetour`,
  `StateToPresetIndex`, `ApplyPreset(int)`.
- `RuleSetPolicyValidator.ValidateEnabled` обращается к файловой системе (`File.Exists`).

### 3.5. Нарушение границ слоёв в WPF

WPF использует Infrastructure напрямую (лог, поиск приложений, иконка трея) — нет портов. `App.xaml.cs` совмещает
composition root, трей, таймеры, single-instance, тему и выход. `SettingsViewModel` получает 8 зависимостей и
отвечает за DNS, TUN, приложения, rule-set, прокси, журнал и поведение окна одновременно.

### 3.6. Infrastructure зависит от WinForms

`UseWindowsForms` в `NothingVpn.Infrastructure.csproj` нужен ради `TunAppIconCache` (используется только Tray),
`Elevation.RestartElevated` (`System.Windows.Forms.Application.ExecutablePath`; можно заменить на
`Environment.ProcessPath`) и `System.Drawing` в `TrayStatusIconBuilder` (UI-код в Infrastructure).

### 3.7. Дублирование между WinForms и WPF

`SingleInstance` и разбор аргументов (`StartupOptions` / `StartupArgs`) существуют в двух копиях. После удаления
`NothingVpn.Tray` уходят вместе с ним, но `InternalsVisibleTo("NothingVpn.Tray")`, `AppMutex` для Tray в `.iss`
и `[InstallDelete]` для `NothingVpn.Tray.exe` нужно пересмотреть осознанно (последние два защищают обновление со
старых версий).

### 3.8. Мелочи

- `Application/Class1.cs` — пустой шаблонный класс.
- `AppLifecycleService.BuildTakeoverArgs` дублирует формирование аргументов в `VpnConnectionService.ConnectAsync`.
- `DiagnosticsService.ExportLogs`, `IAppUpdateController.GetCurrentReleaseAsync`, `AppPathsModel.LogsDir`,
  `AppState.DebugLogs` в WPF не используются.
- Часть WPF-файлов записана «в строку» (`StartupOptions`, `ThemeManager`, `DiagnosticsViewModel`, `UpdateViewModel`,
  `DiagnosticsView`, `SettingsView.xaml`, `Theme.xaml`): плохо читаются и дают огромные диффы.
- `StorageIssueRegistry` — статическая глобальная очередь.
- Каждый вызов стора — полная загрузка и расшифровка файла; кэша нет.
- Фейки в тестах продублированы по классам; общего набора тестовых двойников нет.

## 4. Разрывы функциональности WPF относительно WinForms

WinForms-клиент ещё в решении как эталон (`docs/WPF-MIGRATION.md`). В WPF отсутствуют:

- переключатель отладочного журнала и фильтр уровня в диагностике (WPF показывает весь буфер);
- журнал событий самого приложения (`AppLogger`) — в WPF в журнал пишет только sing-box;
- отдельное окно прогресса загрузки обновления (в WPF — прогресс-бар в настройках).

Решение (18.10.2026): окно «Что нового» переносится в WPF обязательно (уже сделано, см. ниже). Остальные пункты не решены — вернуться к
ним до удаления `NothingVpn.Tray`.

Уже перенесено:

- окно «Что нового» после обновления: `UpdateViewModel.CheckAtStartupAsync` при `InstalledVersionTransition.Upgraded`
  берёт `IAppUpdateController.GetChangelogAsync` (ошибка загрузки → `ReleaseChangelog.LoadFailed`, не исключение)
  и показывает `Views/ReleaseChangelogWindow`. Тело релиза выводится как есть, без рендера markdown; при
  `--smoke-test` проверка обновлений не запускается, поэтому окно не появляется.

## 4а. Открытые решения

- **Безопасность** (исполняемые файлы в пользовательском каталоге, отсутствие проверки подписи установщика,
  см. [SECURITY.md](SECURITY.md)) — обсуждается отдельно, в этот рефакторинг не входит.
- **Каталог данных `NothingVpn.Tray`** — рекомендуется оставить имя: пользователь его не видит, а переименование
  требует миграции, затрагивает установщик и ломает откат на старую версию (старый клиент не найдёт данные).
  Если переименовывать, то копированием со старым каталогом как резервом и только вместе с правкой `.iss`.
  Ждёт подтверждения.

## 5. Устаревшие артефакты в репозитории

- `.cursor/rules/nothing-vpn-singbox-architecture.mdc` описывает UI как WinForms.
- `build-app.cmd` выводит путь к `NothingVpn.Tray.exe`, хотя публикуется `NothingVpn.Desktop.Wpf.exe`.
- Версия `0.6.8` захардкожена в пяти местах: два csproj, `Build.ps1` (значение по умолчанию для установщика),
  `.iss`, `release.yml` (значение по умолчанию для ручного запуска). Кандидат на `Directory.Build.props`.
- `docs/WPF-MIGRATION.md` описывает план миграции, который в основном выполнен.

## 6. Предлагаемый порядок

1. **Страховочная сетка.** ✅ Готово: характеризационные тесты генератора и хранения ([TESTING.md](TESTING.md#характеризационные-тесты)).
   Без них дальнейшие шаги не проверяемы.
2. **Модель состояния.** Убрать перезапись целого состояния (2.1), отделить runtime-флаги подключения, сделать
   `IsRunning` дешёвым (2.2), убрать запись при чтении (2.3).
3. **Одна модель данных.** Генератор и стора на одной модели; удалить `LegacyModelMapper` или закрыть его
   roundtrip-тестом; одна точка нормализации и значений по умолчанию (3.1, 3.2).
4. **Типизация.** Enum'ы для режимов/DNS/действий с сохранением строк в JSON (3.3); очистить Domain от UI (3.4).
5. **Границы WPF.** Порты для журнала и поиска приложений, вынести composition root из `App`, разделить
   `SettingsViewModel`, глобальная обработка исключений (2.4, 3.5).
6. **Удаление WinForms.** После закрытия разрывов (раздел 4): удалить `NothingVpn.Tray`, `TunAppIconCache`,
   зависимость Infrastructure от WinForms (3.6, 3.7).
7. **Сборка.** Единая версия, исправление `build-app.cmd`, актуализация `.cursor`-правил (раздел 5).

Каждый шаг проверяется `.\build\Build.ps1 -Target Test` и ручным чек-листом [UI-REGRESSION-CHECKLIST.md](UI-REGRESSION-CHECKLIST.md)
для затронутых сценариев.

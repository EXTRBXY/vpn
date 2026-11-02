# CLAUDE.md

Nothing VPN — Windows-клиент VLESS на .NET 8: WPF-оболочка над `sing-box.exe`. Интерфейс передаёт типизированный
запрос службе Windows `NothingVpn`, которая строит JSON-конфиг sing-box и запускает его (для XHTTP и REALITY — ещё
`xray.exe` как выход к серверу за локальным SOCKS5, ADR-0002); сам интерфейс управляет
системным прокси WinINET и треем. Режимы: `proxy` (mixed-inbound + системный прокси), `tun` (весь трафик),
`tun_apps` (только выбранные `.exe`).

Язык проекта — русский: UI, сообщения об ошибках для пользователя, документация. Идентификаторы — английские,
комментарии встречаются на обоих языках.

Документация: [docs/README.md](docs/README.md). Рефакторинг завершён (шаги 1–10), дальше продукт только
развивается. Правила, инварианты и подводные камни этого файла обязательны для **любого** изменения — новой функции,
протокола, исправления, рефакторинга. Полный список инвариантов с обоснованием и известные риски —
[docs/REFACTORING.md](docs/REFACTORING.md), раздел 1; прочитайте его перед любой существенной задачей.

## Команды

```powershell
.\build\Build.ps1 -Target Test          # restore + build Release + все тесты + WPF smoke-test (то же, что CI)
dotnet build NothingVpn.sln -c Release
dotnet test NothingVpn.sln -c Release --no-build
dotnet test NothingVpn.sln -c Release --no-build --filter "FullyQualifiedName~SingBoxConfigGeneratorTests"
.\build\Build.ps1 -Target Publish        # artifacts\publish\win-x64\NothingVpn.Desktop.Wpf.exe (+ sing-box.exe, wintun.dll, xray.exe)
.\build\Build.ps1 -Target Installer      # Inno Setup 6 поверх существующего publish
```

Базовая линия (02.11.2026, движок Xray, ADR-0002): сборка без предупреждений, 534 теста проходят, пропущенных нет.
Только Windows. `Publish` берёт `sing-box.exe`/`wintun.dll`/`xray.exe`/`xray-LICENSE.txt` из `-RuntimeAssetsDirectory` или из установленного
клиента (`%ProgramFiles%\NothingVpn`, затем старый `%LOCALAPPDATA%\Programs\NothingVpn`). Ничего не устанавливайте и
не регистрируйте службу на машине пользователя; установку проверяет он сам по `docs/SERVICE-INSTALL-CHECKLIST.md`.

## Структура

```text
src/NothingVpn.Domain          чистые политики и модели настроек (без зависимостей)
src/NothingVpn.Application     use-case сервисы, модели, порты I*Port
src/NothingVpn.Presentation    UI-независимые контроллеры экранов
src/NothingVpn.Infrastructure  реализации портов: sing-box, DPAPI-хранилище, WinINET, обновления;
                               Composition/ApplicationServicesFactory — composition root
src/NothingVpn.Desktop.Wpf     упаковываемый клиент: Composition/DesktopCompositionRoot — UI-композиция,
                               Platform/ — трей, таймеры, тема, single-instance, обработка исключений
src/NothingVpn.Service         служба Windows NothingVpn (LocalSystem): ядро sing-box и канал NothingVpn.Service;
                               ставится установщиком рядом с клиентом; --migrate-user-install — миграция старой
                               per-user установки от имени пользователя
tests/NothingVpn.Application.Tests  единственный тестовый проект, покрывает все слои включая WPF
build/                         Build.ps1, installer/NothingVpn.iss (per-machine, служба)
Directory.Build.props          единственный источник версии продукта
```

Ключевые файлы:

- `Application/Engine/` — граница «интерфейс ↔ ядро»: порт `IVpnEngine`, контракт `EngineConnectRequest`/`EngineStatus`
  (версия `EngineContract.Version`), ядро `SingBoxVpnEngine` (валидация запроса, конфиг, запуск sing-box) — внутри
  клиента и в службе; `IEngineRuleSetStore` — откуда ядро берёт rule-set.
- `Application/Services/VpnConnectionService.cs` — клиентская оркестрация подключения: state.json, WinINET,
  проверка прав для ядра в процессе, откат, восстановление после остановки ядра.
- `Infrastructure/SingBox/SingBoxConfigGenerator.cs` — вся генерация конфига; `SingBoxRunner.cs` — процесс sing-box.
- Движок выхода (ADR-0002): `Domain/Policies/OutboundEnginePolicy.cs` выбирает sing-box или Xray для профиля;
  порт `IOutboundEnginePort`, реализация `Infrastructure/Ports/XrayOutboundPort.cs` и `Infrastructure/Xray/XrayConfigGenerator.cs`;
  общий код процессов — `Infrastructure/Windows/` (`SupervisedProcess`, `ChildProcess`, `ExecutableLocator`).
- `Domain/Policies/*` — правила маршрутизации, DNS, TUN, значения по умолчанию; меняйте поведение здесь, а не в
  генераторе или UI. `Application/Services/AppStateNormalizer.cs` — единственная точка нормализации состояния.
- `Application/Services/SettingsService.cs` — единственный путь записи `state.json` для UI.
- `Infrastructure/Security/DpapiJsonFile.cs` — формат всех файлов данных.
- `Infrastructure/ServiceChannel/*` — протокол канала службы, сервер, клиент `PipeVpnEngine`, ACL и проверка сервера;
  `Infrastructure/ServiceHost/ServiceEngineHost.cs` — composition root службы. Протокол — ADR-0001, шаг 7а.
  Клиент по умолчанию работает через службу (шаг 7б); ядро внутри процесса — только при `NOTHINGVPN_ENGINE=inprocess`
  (разработка). `Infrastructure/Installation/LegacyUserInstallMigration.cs` — переход со старой per-user установки.

## Архитектурные правила

- Зависимости внутрь: Domain ← Application ← Presentation ← WPF. Infrastructure реализует порты Application.
  WPF берёт Infrastructure только в `Composition/DesktopCompositionRoot` (проверяет `WpfLayerBoundaryTests`);
  view model'ям нужное — через порты Application (`ILogPort`, `IAppDiscoveryPort`) и контроллеры Presentation.
- Логику не класть в окна и code-behind; диалоги, буфер обмена, файловые пикеры — допустимы в code-behind.
- sing-box — источник истины для туннелирования. Не дублировать в C# то, что делает ядро (DNS, маршрутизация).
  Новое поведение — политика в Domain, компилируемая в поля конфига. Поля проверять по документации sing-box
  версии из `.github/workflows/release.yml` (`SING_BOX_VERSION`), поля Xray — по версии `XRAY_VERSION`.
- Порядок route-правил значим (first match). Пользовательские rule-set идут до QUIC/IPv6-фолбэков и до
  `process_path`. См. `docs/SINGBOX-CONFIG.md`.

## Новая функциональность

- Новая функция — развитие этого же продукта Nothing VPN, а не форк: не переименовывать продукт, exe, службу, канал,
  мьютекс, каталоги, TUN-интерфейс, `AppId`; не делать установку «рядом» и не отключать автообновление. Всё это —
  инварианты ниже; их изменение — отдельное согласованное решение с ADR и миграцией, не часть фичи.
- Одна функция — одна ветка и один PR. Не смешивать с несвязанными правками (значения по умолчанию, версия, второй
  установщик). Версию в `Directory.Build.props` меняет только релиз. Установщик один — `build/installer/NothingVpn.iss`.
- Новый протокол или транспорт: сначала проверить, поддерживает ли его официальный sing-box версии `SING_BOX_VERSION`.
  - Поддерживает — поля профиля, политика в Domain, поля генератора и golden-тесты конфига.
  - Не поддерживает — это архитектурное решение (второе ядро рядом с sing-box, сборка-форк sing-box, ждать
    upstream). До кода — ADR в `docs/adr/` с вариантами и риск-анализом, согласованный с владельцем. В том же PR —
    точка расширения (отдельный компонент, а не ветки `if` в `SingBoxConfigGenerator`/`SingBoxRunner`), поставка
    бинарника в `release.yml` и `NothingVpn.iss` (фиксированная версия, контрольная сумма, лицензия).
- Служба не доверяет клиенту: каждое новое поле `EngineConnectRequest` проверяется в `SingBoxVpnEngine.Validate`
  через Domain-политику. Сырые JSON-фрагменты в конфиг ядра не передаются — только типизированные поля.
- Значимые сетевые параметры и их значения по умолчанию — константы Domain-политик, не литералы в Infrastructure.
- Готово, когда `.\build\Build.ps1 -Target Test` зелёный (все тесты и smoke-test) и обновлены затронутые документы:
  `docs/SINGBOX-CONFIG.md`, `ARCHITECTURE.md`, `SECURITY.md`, `STORAGE.md`, ADR-0001 для полей контракта.

## Инварианты, которые нельзя ломать без миграции

- Каталог данных `%LOCALAPPDATA%\NothingVpn.Tray` (историческое имя).
- Значения `[JsonPropertyName]` в `AppStateModel`, `UserRuleSetModel`, `ProxySettingsSnapshotModel`, `VpnProfile`
  и имена свойств `SubscriptionRecord` — это схема JSON на диске (camelCase).
- Строковые значения enum-настроек в состоянии: `proxy|tun|tun_apps`, `doh|system`, DNS detour `direct|proxy`,
  действие rule-set `direct|block`, стек `""|system|mixed|gvisor` (`""` — по умолчанию), `tray|exit`,
  `trace…panic`, движок профиля `auto|sing-box|xray`, режим XHTTP `auto|packet-up|stream-up|stream-one`.
  В коде это enum'ы; строки задаёт `SettingValueMap<T>` в Domain-политиках, чтение state.json
  толерантное (неизвестное → значение по умолчанию, не исключение). Enum в строку — только `ToValue()`, не `ToString()`.
- `VlessLinkParser.StableId` — Id профиля; на нём держатся активный профиль и синхронизация подписок.
- Хеш profileId → адреса TUN; `BuiltinId`/`Tag` встроенных rule-set.
- Аргументы `--start --mode --profile`, `--smoke-test` (`--takeover` удалён на шаге 8 и игнорируется);
  мьютекс `Global\NothingVpn.Desktop.Wpf` (он же `AppMutex` установщика); имя `NothingVpn.Desktop.Wpf.exe`;
  ассет `NothingVpnSetup.exe` в `EXTRBXY/vpn`.
- Служба `NothingVpn`, канал `NothingVpn.Service`, каталог установки `%ProgramFiles%\NothingVpn` (только он: служба
  от SYSTEM запускает файлы оттуда), `AppId` установщика, `NothingVpn.Service.exe --migrate-user-install`.

## Подводные камни

- Одна модель данных: Application-модели сериализуются в `state.json`/`profiles.json` напрямую. Новое поле — свойство
  с `[JsonPropertyName]` плюс заполнение в `StorageRoundtripTests`; для подписок — ещё `SubscriptionRecord` и оба
  метода `SubscriptionStorePort`. Пропуск ловят тесты хранения.
- Нормализация — только `AppStateNormalizer` (Domain-политики), значения по умолчанию — константы политик.
  Генератор sing-box получает нормализованное состояние и не нормализует сам; не добавляйте туда исправление входов.
- `state.json` пишется только через общий `AppStateStore` (один экземпляр на процесс, один лок): UI —
  `ISettingsService.UpdateState(s => s.Field = …)`, подключение — `AppStateStore.Update` с конкретными полями.
  Записи целого устаревшего объекта (`SaveState`) больше нет — не возвращайте её.
- Runtime-флаги `ProxyWasEnabledByUs`/`PreviousProxySettings` пишет только `VpnConnectionService`;
  `SettingsService.UpdateState` их не меняет (`ConnectionRuntimeState`), даже если колбэк их присвоил.
- `GetStatus`/`IsRunning` не читают диск: процесс — из `IVpnEngine.GetStatus`, режим/профиль — из сессии в памяти.
- sing-box запускается только через `IVpnEngine` — это и контракт канала службы. Запрос к ядру типизированный:
  никакого готового JSON sing-box и путей, куда ядро пишет. Новое поле, нужное ядру, — в `EngineConnectRequest`
  (с `[JsonPropertyName]`) и `EngineRequestMapper`; несовместимое изменение — с ростом `EngineContract.Version`
  (сейчас 3) и записью в ADR-0001. Поля конверта канала `contractVersion`/`type`/`id`/`error` не меняются никогда.
- Клиент канала проверяет сервер (`LocalSystemPipeServerVerifier`) до отправки первого байта — не ослабляйте и не
  обходите проверку; в тестах её подменяет внутренний `IPipeServerVerifier`.
  WinINET и state.json остаются в `VpnConnectionService`, а не в ядре.
- Секреты в журнал — только через `ILogPort.Append` / `InMemoryLogStore.AppendStructured` (маскирует `LogRedactor`).
  Никогда не логировать UUID, ключи, URL подписок. События клиента пишутся с источником из `AppLogSources`
  (`app/connection`, `app/subscriptions`, …) и не в горячих путях (таймеры раз в секунду ничего не пишут). Адрес
  сервера и URL подписки без `/sub/` редактор не узнаёт — не передавайте их в журнал вовсе (см. `SubscriptionService`).
- Infrastructure не зависит от WinForms/WindowsDesktop (служба без WindowsDesktop runtime); DPAPI — пакет
  `System.Security.Cryptography.ProtectedData`. UI-код с `System.Drawing`/`System.Windows.Forms` — только в WPF.
- Необработанные исключения WPF ловит `Platform/GlobalExceptionHandler` (журнал + сообщение; фатальные — попытка
  остановить VPN). Запуск задачи без ожидания с UI-потока — `task.Forget()`, не `_ = task`: так ошибка не теряется.
- Страница настроек разделена на разделы (`ViewModels/Settings/*`), но сохраняется одной операцией в
  `SettingsViewModel.Save`. Новое поле настроек — в раздел (`Load`/`ToSettings`) и в черновик сохранения.
- Конфиг sing-box в `configs\` лежит открытым текстом; он обязан удаляться при остановке и откате.
- TUN через службу прав интерфейсу не требует (`IVpnEngine.TunRequiresElevatedCaller`). Интерфейс себя не повышает
  (шаг 8, [docs/adr/0001-privileged-service.md](docs/adr/0001-privileged-service.md)): ядро в процессе
  (`NOTHINGVPN_ENGINE=inprocess`) без прав отклоняет TUN ошибкой. Не возвращайте `runas`/`--takeover`.
  Недоступная служба — ошибка пользователю, а не откат на ядро в процессе.
- Перезапущенный интерфейс подхватывает сессию, работающую в службе: `RecoverStaleRuntimeState` её не трогает, флаги
  прокси остаются в state.json до отключения.
- Установщик повышен: пользовательское (HKCU, `%LOCALAPPDATA%`, ярлыки пользователя) — только через
  `ExecAsOriginalUser` (`--migrate-user-install`), не из повышенного `[Code]`. Старый деинсталлятор ≤ 0.6.8 не
  запускать: в тихом режиме он удаляет данные.
- Установщик обновления запускает `InstallerLaunchService` после штатного выхода клиента (VPN отключён, мьютекс
  освобождён): `Process.Start` с `UseShellExecute=false`, имя файла по `InstallerFilePolicy`, отказ, если процесс
  клиента повышен. Не добавлять `runas`, `UseShellExecute`, запуск до выхода. См. `docs/UPDATE-HANDOFF.md`.
- WPF smoke-test и запуск клиента используют настоящий каталог данных текущего пользователя.

## Стиль

- C# 12, nullable включён, file-scoped namespaces, primary constructors допустимы. Код самодокументируемый,
  комментарии — только про неочевидные причины.
- Нормализуйте и валидируйте входные значения через Domain-политики, а не локально.
- Сетевые HTTP-клиенты ставят явный `UseProxy = false`, если запрос не должен идти через собственный VPN.
- Тестовые фейки объявляются внутри тестового класса. Для WPF-тестов нужен STA-поток (см. `WpfSettingsBindingTests`).
- Версия продукта — только `Version` в `Directory.Build.props` (все сборки; `Build.ps1` передаёт её в publish и
  Inno Setup, у `.iss` своей копии нет). Не задавайте версию в csproj.
- Релиз: сменить версию в `Directory.Build.props`, влить в `main`, push тега `vX.Y.Z` с той же версией → GitHub
  Actions сверяет тег с props, собирает и публикует установщик (DEVELOPMENT.md, «Версия и релиз»).

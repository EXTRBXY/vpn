# CLAUDE.md

Nothing VPN — Windows-клиент VLESS на .NET 8: WPF-оболочка над `sing-box.exe`. Клиент генерирует JSON-конфиг
sing-box, запускает его дочерним процессом и управляет системным прокси WinINET, UAC и треем. Режимы: `proxy`
(mixed-inbound + системный прокси), `tun` (весь трафик), `tun_apps` (только выбранные `.exe`).

Язык проекта — русский: UI, сообщения об ошибках для пользователя, документация. Идентификаторы — английские,
комментарии встречаются на обоих языках.

Документация: [docs/README.md](docs/README.md). Перед рефакторингом обязательно прочитать
[docs/REFACTORING.md](docs/REFACTORING.md) — там инварианты и известные риски.

## Команды

```powershell
.\build\Build.ps1 -Target Test          # restore + build Release + все тесты + WPF smoke-test (то же, что CI)
dotnet build NothingVpn.sln -c Release
dotnet test NothingVpn.sln -c Release --no-build
dotnet test NothingVpn.sln -c Release --no-build --filter "FullyQualifiedName~SingBoxConfigGeneratorTests"
.\build\Build.ps1 -Target Publish        # artifacts\publish\win-x64\NothingVpn.Desktop.Wpf.exe (+ sing-box.exe, wintun.dll)
.\build\Build.ps1 -Target Installer      # Inno Setup 6 поверх существующего publish
.\build\Test-UpdateHandoff.ps1           # desktop-тесты обновления; повторить с -RequireElevated из админ-терминала
```

Базовая линия (18.10.2026): сборка без предупреждений, 274 теста проходят, 3 desktop-теста пропущены намеренно.
Только Windows. `Publish` берёт `sing-box.exe`/`wintun.dll` из `-RuntimeAssetsDirectory` или из установленного
клиента в `%LOCALAPPDATA%\Programs\NothingVpn`.

## Структура

```text
src/NothingVpn.Domain          чистые политики и модели настроек (без зависимостей)
src/NothingVpn.Application     use-case сервисы, модели, порты I*Port
src/NothingVpn.Presentation    UI-независимые контроллеры экранов
src/NothingVpn.Infrastructure  реализации портов: sing-box, DPAPI-хранилище, WinINET, UAC, обновления;
                               Composition/ApplicationServicesFactory — composition root
src/NothingVpn.Desktop.Wpf     упаковываемый клиент (App.xaml.cs — UI-композиция, трей, таймеры)
src/NothingVpn.Tray            legacy WinForms, эталон до удаления; не публикуется
tests/NothingVpn.Application.Tests  единственный тестовый проект, покрывает все слои включая WPF
build/                         Build.ps1, Test-UpdateHandoff.ps1, installer/NothingVpn.iss
```

Ключевые файлы:

- `Application/Services/VpnConnectionService.cs` — подключение/отключение, откат, восстановление после падения.
- `Infrastructure/SingBox/SingBoxConfigGenerator.cs` — вся генерация конфига; `SingBoxRunner.cs` — процесс sing-box.
- `Domain/Policies/*` — правила маршрутизации, DNS, TUN; меняйте поведение здесь, а не в генераторе или UI.
- `Application/Services/SettingsService.cs` — единственный путь записи `state.json` для UI.
- `Infrastructure/Security/DpapiJsonFile.cs` — формат всех файлов данных.

## Архитектурные правила

- Зависимости внутрь: Domain ← Application ← Presentation ← WPF. Infrastructure реализует порты Application.
  WPF должен брать Infrastructure только в composition root (сейчас нарушено для журнала, поиска приложений и иконки
  трея — не расширяйте эти нарушения, добавляйте порты).
- Логику не класть в окна и code-behind; диалоги, буфер обмена, файловые пикеры — допустимы в code-behind.
- sing-box — источник истины для туннелирования. Не дублировать в C# то, что делает ядро (DNS, маршрутизация).
  Новое поведение — политика в Domain, компилируемая в поля конфига. Поля проверять по документации sing-box
  версии из `.github/workflows/release.yml` (`SING_BOX_VERSION`).
- Порядок route-правил значим (first match). Пользовательские rule-set идут до QUIC/IPv6-фолбэков и до
  `process_path`. См. `docs/SINGBOX-CONFIG.md`.

## Инварианты, которые нельзя ломать без миграции

- Каталог данных `%LOCALAPPDATA%\NothingVpn.Tray` (историческое имя).
- Имена свойств `AppState`, `VlessProfile`, `UserRuleSet`, `SubscriptionRecord`, `WinInetProxySettingsSnapshot`
  в Infrastructure — это схема JSON на диске (camelCase).
- Строковые значения в состоянии: `proxy|tun|tun_apps`, `doh|system`, `direct|proxy|block`, `tray|exit`.
- `VlessLinkParser.StableId` — Id профиля; на нём держатся активный профиль и синхронизация подписок.
- Хеш profileId → адреса TUN; `BuiltinId`/`Tag` встроенных rule-set.
- Аргументы `--takeover --start --mode --profile`, `--smoke-test`; мьютекс `Global\NothingVpn.Desktop.Wpf`;
  имя `NothingVpn.Desktop.Wpf.exe`; ассет `NothingVpnSetup.exe` в `EXTRBXY/vpn`.

## Подводные камни

- Две модели данных: Application (`AppStateModel`, `VpnProfile`, …) и Infrastructure-legacy (`AppState`,
  `VlessProfile`, …) с ручным `LegacyModelMapper`. Новое поле нужно добавить в обе модели и в оба направления
  маппера (а для подписок — в `SubscriptionStorePort`), иначе оно молча не сохранится.
- Записывайте состояние через `ISettingsService.UpdateState(s => s.Field = …)`. `SaveState(wholeState)` с устаревшей
  копией затирает runtime-флаги `ProxyWasEnabledByUs`/`PreviousProxySettings` и параллельные изменения.
- `VpnConnectionService` пишет состояние через `IStateStorePort` напрямую, минуя `SettingsService`.
- `ConnectionController.IsRunning` читает и расшифровывает `state.json` при каждом вызове.
- Секреты в журнал — только через `InMemoryLogStore.AppendStructured` (маскирует `LogRedactor`). Никогда не логировать
  UUID, ключи, URL подписок.
- Конфиг sing-box в `configs\` лежит открытым текстом; он обязан удаляться при остановке и откате.
- TUN требует админа: клиент перезапускает себя через `runas --takeover`, старый процесс выходит. Не пытаться
  «повысить на месте».
- Обновление из повышенного клиента запускается только через неповышенный токен (`WindowsUserProcessLauncher`).
  Не добавлять запасной повышенный путь, `runas`, `UseShellExecute` для установщика. См. `docs/UPDATE-HANDOFF.md`.
- WPF smoke-test и запуск клиента используют настоящий каталог данных текущего пользователя.

## Стиль

- C# 12, nullable включён, file-scoped namespaces, primary constructors допустимы. Код самодокументируемый,
  комментарии — только про неочевидные причины.
- Нормализуйте и валидируйте входные значения через Domain-политики, а не локально.
- Сетевые HTTP-клиенты ставят явный `UseProxy = false`, если запрос не должен идти через собственный VPN.
- Тестовые фейки объявляются внутри тестового класса. Для WPF-тестов нужен STA-поток (см. `WpfSettingsBindingTests`).
- Версия (`0.6.8`) захардкожена в обоих csproj, `Build.ps1`, `.iss`, `release.yml` — при смене версии менять везде.
- Релиз: push тега `vX.Y.Z` → GitHub Actions собирает и публикует установщик.

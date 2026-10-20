# Development

## Структура репозитория

```text
src/          исходный код приложения
tests/        автоматические тесты
build/        единая локальная и CI-сборка, Inno Setup
artifacts/    результаты сборки (не добавляются в Git)
```

Проекты также сгруппированы в одноимённые папки внутри `NothingVpn.sln`.

Границы слоёв описаны в [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Единственный клиент — WPF
(`NothingVpn.Desktop.Wpf`); WinForms-клиент удалён на шаге 9 рефакторинга (история перехода —
[docs/WPF-MIGRATION.md](docs/WPF-MIGRATION.md)).
Полный указатель технической документации — [docs/README.md](docs/README.md).

## Единая сборка

### Быстрая ручная проверка

Для обычной проверки приложения не требуется запускать автоматические тесты:

1. Запустите `build-app.cmd` двойным кликом. Скрипт создаст опубликованную self-contained сборку без установщика и откроет её каталог.
2. Запустите и проверьте `artifacts\publish\win-x64\NothingVpn.Desktop.Wpf.exe`. Клиент подключается через службу
   `NothingVpn`: без установленной службы подключение покажет «Служба Nothing VPN не отвечает…». Для проверки без
   установки включите ядро в процессе (`NOTHINGVPN_ENGINE=inprocess`, см. «Служба Windows»).
3. После проверки запустите `build-installer.cmd`. Он упакует в Inno Setup именно существующую опубликованную сборку, не пересобирая её.
4. Проверьте `artifacts\installer\NothingVpnSetup.exe` (ставит клиент и службу в `C:\Program Files\NothingVpn`,
   нужен UAC). Полная ручная проверка — [docs/SERVICE-INSTALL-CHECKLIST.md](docs/SERVICE-INSTALL-CHECKLIST.md).

`build-installer.cmd` намеренно завершается ошибкой, если опубликованная сборка ещё не создана или в ней нет `sing-box.exe`.

Все локальные и CI-сценарии запускаются через `build/Build.ps1` из корня репозитория:

```powershell
.\build\Build.ps1 -Target Clean
.\build\Build.ps1 -Target Build
.\build\Build.ps1 -Target Test
.\build\Build.ps1 -Target Publish
.\build\Build.ps1 -Target Installer   # упаковать существующий publish
.\build\Build.ps1 -Target All
```

Цели `Build`, `Test` и `Publish` самостоятельно выполняют необходимый restore. `Test` после автоматических тестов также запускает короткий smoke-test WPF-клиента. `Installer` только упаковывает уже проверенный publish. `All` очищает артефакты и выполняет полный цикл, включая тесты.

Старые `publish.ps1` и `publish.cmd` оставлены как совместимые оболочки для `-Target Publish`.

## Результаты

```text
artifacts/publish/win-x64/             опубликованное приложение
artifacts/test-results/                TRX-результаты тестов
artifacts/installer/NothingVpnSetup.exe
artifacts/installer/NothingVpnSetup.exe.sha256
```

Промежуточные `bin` и `obj` остаются стандартными каталогами MSBuild, но готовые результаты больше не извлекаются из них.

## Runtime-зависимости

Для установленного приложения рядом с `NothingVpn.Service.exe` необходим `sing-box.exe`; для TUN также нужен
`wintun.dll`. Служба берёт их только из каталога своего exe.

Локальный `Publish` ищет их в установленном клиенте: `%ProgramFiles%\NothingVpn`, затем старый per-user каталог
`%LOCALAPPDATA%\Programs\NothingVpn`. Можно явно передать каталог:

```powershell
.\build\Build.ps1 -Target Publish -RuntimeAssetsDirectory C:\path\to\runtime-assets
```

`Installer` требует `NothingVpn.Desktop.Wpf.exe`, `NothingVpn.Service.exe` и `sing-box.exe`; без `wintun.dll`
установщик собирается, но TUN будет недоступен.

## Служба Windows

С шага 7б интерфейс работает через службу `NothingVpn` (`src/NothingVpn.Service`, LocalSystem) по каналу
`\\.\pipe\NothingVpn.Service`; архитектура — [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#подключение-через-службу-шаг-7б),
протокол — [ADR-0001](docs/adr/0001-privileged-service.md).

**Сборка.** `Build.ps1 -Target Publish` публикует клиент и службу в один каталог `artifacts\publish\win-x64`
(обе — self-contained single-file). Консольного режима у службы нет: клиент проверяет, что сервер канала — служба
LocalSystem в сеансе 0, поэтому запуск exe из терминала (даже администратора) ничего не даст.

**Установка для проверки** — только через собранный установщик: он регистрирует службу с путём в
`Program Files`, запускает её и останавливает при обновлении.

```powershell
.\build\Build.ps1 -Target Publish
.\build\Build.ps1 -Target Installer
.\artifacts\installer\NothingVpnSetup.exe
```

Не регистрируйте службу вручную на exe из рабочего каталога (`sc.exe create … binPath= D:\…`): служба от SYSTEM
запускала бы файлы из каталога, куда может писать любой ваш процесс.

**Состояние и журналы.**

```powershell
sc.exe query NothingVpn          # STATE: 4 RUNNING
sc.exe qc NothingVpn             # путь, автозапуск, LocalSystem
sc.exe qfailure NothingVpn       # действия при сбое
Get-WinEvent -LogName Application -MaxEvents 20 | Where-Object ProviderName -eq NothingVpn
```

Журнал ядра службы (вывод sing-box, ошибки запуска) приходит в журнал интерфейса — раздел «Диагностика». Журнал
установщика — `%TEMP%\Setup Log *.txt` пользователя, подтвердившего UAC.

**Отладка.** Установите Debug/Release-сборку установщиком и подключите отладчик Visual Studio, запущенной от имени
администратора, к процессу `NothingVpn.Service.exe` (Debug → Attach to Process, «Show processes from all users»).
Код ядра одинаков в службе и в процессе клиента, поэтому большую часть отладки проще вести с ядром в процессе.
Транспорт канала покрывают `ServiceChannelTests` (сервер и клиент в одном процессе).

**Ядро в процессе клиента (без службы).** Для разработки и ручной проверки без установки:

```powershell
$env:NOTHINGVPN_ENGINE = 'inprocess'
.\artifacts\publish\win-x64\NothingVpn.Desktop.Wpf.exe
```

Тогда sing-box запускает сам клиент (как до шага 7б), ищет его рядом с exe, в `bin\` и родительских каталогах, а
TUN требует прав администратора у самого клиента. Клиент себя не повышает (с шага 8 UAC-перезапуска `--takeover` нет):
без прав подключение TUN завершается ошибкой «Ядро в процессе: для TUN запустите клиент от имени администратора.».
Для TUN запускайте клиент из терминала, открытого от имени администратора (переменная окружения должна быть задана
в нём же). Режим proxy работает и без прав. Обновление из повышенного клиента отклоняется — установщик унаследовал
бы права. Любое другое значение переменной (или её отсутствие) — служба. В журнал пишется предупреждение о режиме.

## Сборка установщика

Сначала соберите и проверьте опубликованную сборку приложения, затем установите Inno Setup 6 и выполните:

```powershell
.\build\Build.ps1 -Target Installer
```

При нестандартном расположении компилятора:

```powershell
.\build\Build.ps1 -Target Installer -InnoCompilerPath C:\Tools\InnoSetup\ISCC.exe
```

Конфигурация находится в `build/installer/NothingVpn.iss`. Пути publish/output и версию передаёт ей build-скрипт;
пути не привязаны к `bin` проекта, а без переданной версии `.iss` не компилируется (своей копии версии в нём нет).

## GitHub Actions и релизы

Запуск установщика обновления из интерфейса описан в [docs/UPDATE-HANDOFF.md](docs/UPDATE-HANDOFF.md).

- `CI` вызывает `build/Build.ps1 -Target Test` на push в `main` и в pull request.
- `Release` скачивает зафиксированные версии sing-box/Wintun и вызывает тот же build-скрипт.
- SHA-256 runtime-архивов проверяется до распаковки; GitHub Actions закреплены по commit SHA.
- Вместе с установщиком создаётся и публикуется `NothingVpnSetup.exe.sha256`.
- Push тега `v*` создаёт GitHub Release с `NothingVpnSetup.exe`.
- Ручной запуск workflow сохраняет установщик как artifact.

## Версия и релиз

Версия продукта задана в одном месте — `Version` в [Directory.Build.props](Directory.Build.props) в корне. Её
получают все сборки решения (клиент и служба ставятся одним установщиком и обязаны совпадать; у библиотек своего
цикла выпуска нет); `InformationalVersion` равна ей, без суффикса коммита. Версия в разделе «Обновления» и
User-Agent клиента берутся из версии входной сборки, то есть отсюда же.

`Build.ps1` без `-Version` читает версию из `Directory.Build.props` и передаёт её и в `dotnet publish`, и в Inno
Setup. `-Version` переопределяет её для одной сборки (например, тестовой), файл при этом не меняется:

```powershell
.\build\Build.ps1 -Target Publish -Version 9.9.9
.\build\Build.ps1 -Target Installer -Version 9.9.9
```

Выпуск релиза:

1. Поменять `Version` в `Directory.Build.props`, закоммитить и влить в `main` (через PR с зелёным CI).
2. Поставить на этот коммит тег с той же версией и запушить его:

   ```powershell
   git tag v0.7.0
   git push origin v0.7.0
   ```

`Release` сверяет тег с `Directory.Build.props` и падает с понятной ошибкой, если они расходятся (тогда удалите тег,
исправьте версию и поставьте тег заново). После сборки он проверяет, что `ProductVersion` у
`NothingVpn.Desktop.Wpf.exe` и `NothingVpn.Service.exe` равна версии релиза. Ручной запуск workflow с пустым
параметром берёт версию из `Directory.Build.props`.

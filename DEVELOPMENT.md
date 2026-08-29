# Development

## Структура репозитория

```text
src/          исходный код приложения
tests/        автоматические тесты
build/        единая локальная и CI-сборка, Inno Setup
artifacts/    результаты сборки (не добавляются в Git)
```

Проекты также сгруппированы в одноимённые папки внутри `NothingVpn.sln`.

Границы слоёв и последовательность перехода с WinForms на WPF описаны в [docs/WPF-MIGRATION.md](docs/WPF-MIGRATION.md).

## Единая сборка

### Быстрая ручная проверка

Для обычной проверки приложения не требуется запускать автоматические тесты:

1. Запустите `build-app.cmd` двойным кликом. Скрипт создаст опубликованную self-contained сборку без установщика и откроет её каталог.
2. Запустите и проверьте `artifacts\publish\win-x64\NothingVpn.Tray.exe`.
3. После проверки запустите `build-installer.cmd`. Он упакует в Inno Setup именно существующую опубликованную сборку, не пересобирая её.
4. Проверьте `artifacts\installer\NothingVpnSetup.exe`.

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

Цели `Build`, `Test` и `Publish` самостоятельно выполняют необходимый restore. `Installer` только упаковывает уже проверенный publish. `All` очищает артефакты и выполняет полный цикл, включая тесты.

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

Для установленного приложения рядом с `NothingVpn.Tray.exe` необходим `sing-box.exe`; для TUN также нужен `wintun.dll`.

Локальный `Publish` ищет их в `%LOCALAPPDATA%\Programs\NothingVpn`. Можно явно передать каталог:

```powershell
.\build\Build.ps1 -Target Publish -RuntimeAssetsDirectory C:\path\to\runtime-assets
```

`Installer` требует `sing-box.exe` и предупреждает при отсутствии `wintun.dll`.

## Сборка установщика

Сначала соберите и проверьте опубликованную сборку приложения, затем установите Inno Setup 6 и выполните:

```powershell
.\build\Build.ps1 -Target Installer -Version 0.5.9
```

При нестандартном расположении компилятора:

```powershell
.\build\Build.ps1 -Target Installer `
  -Version 0.5.9 `
  -InnoCompilerPath C:\Tools\InnoSetup\ISCC.exe
```

Конфигурация находится в `build/installer/NothingVpn.iss`. Пути publish/output передаются ей build-скриптом и не привязаны к `bin` проекта.

## GitHub Actions и релизы

- `CI` вызывает `build/Build.ps1 -Target Test` на push в `main` и в pull request.
- `Release` скачивает зафиксированные версии sing-box/Wintun и вызывает тот же build-скрипт.
- SHA-256 runtime-архивов проверяется до распаковки; GitHub Actions закреплены по commit SHA.
- Вместе с установщиком создаётся и публикуется `NothingVpnSetup.exe.sha256`.
- Push тега `v*` создаёт GitHub Release с `NothingVpnSetup.exe`.
- Ручной запуск workflow сохраняет установщик как artifact.

Версия тега передаётся одновременно в `dotnet publish` и Inno Setup:

```powershell
git tag v0.5.9
git push origin v0.5.9
```

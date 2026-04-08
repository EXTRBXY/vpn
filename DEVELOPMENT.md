# Development

Этот документ — для разработчиков.

## Быстрая сборка (publish)

Из корня репозитория:

```powershell
.\publish.ps1
```

или:

```bat
publish.cmd
```

Оба скрипта делают:

```powershell
dotnet publish NothingVpn.Tray\NothingVpn.Tray.csproj -c Release -r win-x64
```

Результат: `NothingVpn.Tray\bin\Release\net8.0-windows\win-x64\publish\`.

## Сборка установщика (Inno Setup) локально

1. Соберите publish (см. выше).
2. Убедитесь, что в `publish` рядом с `NothingVpn.Tray.exe` лежат:
   - `sing-box.exe`
   - при необходимости `wintun.dll`
3. Соберите `installer\NothingVpn.iss` через Inno Setup (`ISCC.exe`).

Готовый установщик появится в `installer\Output\NothingVpnSetup.exe`.

## Релизы на GitHub

В репозитории настроен workflow `Release` (`.github/workflows/release.yml`):

- **push тега `v*`** (например `v0.1.0`) → собирает `NothingVpnSetup.exe` и прикрепляет к GitHub Release.
- **ручной запуск** (*Actions → Release → Run workflow*) → собирает установщик и кладёт в артефакты прогона (без релиза).

### Как выпустить версию

```bash
git tag v0.1.0
git push origin v0.1.0
```

Версия установщика в CI берётся из имени тега (без префикса `v`).

### Обновить версии зависимостей в CI

- **sing-box**: в `.github/workflows/release.yml` переменная `SING_BOX_VERSION` (скачивается архив `sing-box-$ver-windows-amd64.zip`).
- **Wintun**: там же `WINTUN_VERSION` (скачивается `https://www.wintun.net/builds/wintun-$ver.zip`).


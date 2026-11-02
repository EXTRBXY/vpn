# NothingX

Форк [EXTRBXY/vpn](https://github.com/EXTRBXY/vpn) 0.7.0 (Nothing VPN) с поддержкой VLESS + **XHTTP**.
Ставится **рядом** с Nothing VPN и не мешает ему.

## Что отличается от Nothing VPN

| | Nothing VPN | NothingX |
|---|---|---|
| Каталог установки | `%ProgramFiles%\NothingVpn` | `%ProgramFiles%\NothingX` |
| Служба / канал | `NothingVpn` / `NothingVpn.Service` | `NothingX` / `NothingX.Service` |
| Данные пользователя | `%LOCALAPPDATA%\NothingVpn.Tray` | `%LOCALAPPDATA%\NothingX` |
| Данные службы | `%ProgramData%\NothingVpn` | `%ProgramData%\NothingX` |
| Мьютекс | `Global\NothingVpn.Desktop.Wpf` | `Global\NothingX.Desktop` |
| exe | `NothingVpn.Desktop.Wpf.exe`, `NothingVpn.Service.exe` | `NothingX.exe`, `NothingX.Service.exe` |
| Автозапуск (HKCU\…\Run) | `NothingVpn` | `NothingX` |
| TUN-интерфейс | `NothingVpn-…` | `NothingX-…` |
| Запись в «Программах» | AppId `{A2D610D2-…}` | `NothingX` (NSIS) / AppId `{1A441CCD-…}` (Inno) |
| Автообновление | из релизов EXTRBXY/vpn | выключено (ищется ассет `NothingXSetup.exe`, которого там нет) |

Миграция старой per-user установки (`--migrate-user-install`) смотрит на несуществующий ключ
`{480C4BA1-…}_is1`, поэтому установку Nothing VPN не трогает.

## XHTTP

sing-box (1.13) не умеет XHTTP, поэтому для профилей `type=xhttp` (и `splithttp`) служба запускает рядом
официальный Xray-core — `nothingx-xray.exe` (xray.exe из `Xray-windows-64.zip`, v26.3.27):

```
приложения → sing-box (mixed / TUN) → socks5 127.0.0.1:<случайный порт, одноразовый логин/пароль>
           → nothingx-xray.exe → VLESS + XHTTP (+ Reality/TLS) → сервер
```

- `Infrastructure/SingBox/XrayConfigGenerator.cs` — конфиг Xray; `SingBoxConfigGenerator` для xhttp делает
  outbound `socks` и в TUN исключает `nothingx-xray.exe` (process_name) и IP сервера (ip_cidr) — без петли.
- `SingBoxRunner` запускает Xray до sing-box, ждёт его порт, проверяет конфиг (`xray run -test`), гасит sing-box,
  если Xray упал, и удаляет оба конфига при остановке.
- Ссылка: `type=xhttp&path=…&host=…&mode=auto|packet-up|stream-up|stream-one&extra={json}`; `flow` для xhttp
  игнорируется (Vision только для RAW/TCP). Id профилей не-xhttp не изменился.
- Профили tcp/ws/grpc работают как в Nothing VPN (только sing-box).

## Сборка

Windows (как в оригинале, Inno Setup 6): положить `sing-box.exe`, `wintun.dll`, `nothingx-xray.exe` в каталог
и `.\build\Build.ps1 -Target Publish -RuntimeAssetsDirectory <каталог>`, затем `-Target Installer`
(`build/installer/NothingX.iss` → `artifacts/installer/NothingXSetup.exe`).

Linux (так собран выпуск 1.0.0): `dotnet publish` клиента и службы с `-r win-x64 -p:EnableWindowsTargeting=true`
в `artifacts/publish/win-x64`, туда же runtime-файлы и `licenses/`, затем
`makensis -DVERSION=1.0.0 -DPUBLISH_DIR=../../artifacts/publish/win-x64 -DOUT_DIR=../../artifacts/installer build/installer/NothingX.nsi`.
Установщик NSIS повторяет сценарий `.iss`: служба LocalSystem с перезапуском при сбое, остановка службы перед
обновлением, проверка запущенного клиента, ярлыки, автозапуск, удаление с вопросом про данные.

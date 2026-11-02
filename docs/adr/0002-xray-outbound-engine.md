# ADR-0002. Xray как выход к серверу рядом с sing-box

- Статус: принято (02.11.2026)
- Связано: [ADR-0001](0001-privileged-service.md), [SINGBOX-CONFIG.md](../SINGBOX-CONFIG.md),
  [SECURITY.md](../SECURITY.md), [STORAGE.md](../STORAGE.md)

## Контекст

1. **XHTTP.** Транспорт XHTTP (бывший SplitHTTP) — собственная разработка Xray. Официальный sing-box
   (`SING_BOX_VERSION` = 1.13.21) его не поддерживает и не планирует; поддержка есть только в стороннем форке
   sing-box-lx. Подписки всё чаще содержат узлы `type=xhttp`, сейчас клиент их отклоняет.
2. **REALITY.** Официальный sing-box вырезает из ClientHello REALITY ключ X25519MLKEM768, и серверы на Xray
   ≥ 26.9.8 отклоняют такое рукопожатие («reality verification failed», sing-box issue #4520).
3. Ветка `nothingX` решала п. 1 переименованием продукта, вторым установщиком, сырым JSON `extra` и ветвлениями в
   `SingBoxRunner`; она не принята (нарушает инварианты docs/REFACTORING.md, раздел 1).

## Рассмотренные варианты

| Вариант | Итог |
| --- | --- |
| А. Форк sing-box-lx вместо sing-box | Чужой форк ядра, своё расписание релизов и миграций; REALITY-проблема п. 2 остаётся |
| Б. Xray целиком вместо sing-box (TUN, DNS, маршрутизация) | TUN Xray на Windows молод, у пользователей v2rayN — утечки DNS; теряем rule-set, `tun_apps`, всю модель конфига |
| **В. sing-box — всегда вход; Xray — только выход к серверу через локальный SOCKS5** | TUN, DNS, маршрутизация, rule-set и `tun_apps` остаются в sing-box без изменений. Принято |

## Решение

### Схема

```text
приложения → sing-box (mixed / TUN, DNS, route, rule-set)
               └─ outbound "proxy": socks → 127.0.0.1:<случайный порт>, одноразовые логин/пароль
                    └─ Xray: inbound socks (только 127.0.0.1) → outbound vless (+ XHTTP / REALITY / TLS) → сервер
```

- Все настройки TUN, DNS и маршрутизации одинаковы для обоих движков: переключать наборы настроек не нужно.
- У Xray один выход и нет правил маршрутизации, DNS и TUN: всё это решает sing-box.
- В TUN-режиме соединения самого Xray исключаются из TUN правилами `process_path` (путь `xray.exe`) и `ip_cidr`
  (адрес сервера, если это IP) → `direct`. DNS-запросы Xray к имени сервера перехватывает `hijack-dns` и разрешает
  bootstrap-DNS, как имя сервера для sing-box.
- Порт SOCKS выбирается свободный на каждое подключение; логин и пароль — 128 бит из CSPRNG на каждое подключение.
  Оба конфига (sing-box и Xray) лежат в каталоге конфигов ядра и удаляются при остановке и откате.

### Выбор движка

- У профиля есть предпочтение `engine`: `auto` (по умолчанию) | `sing-box` | `xray`. Это пользовательское поле:
  оно не входит в `VlessLinkParser.StableId` и сохраняется при синхронизации подписки и правке ссылки.
- `OutboundEnginePolicy.Resolve` (Domain) выбирает движок: XHTTP поддерживает только Xray; в режиме `auto`
  REALITY идёт через Xray (п. 2), остальное — через sing-box. Явный `sing-box` для XHTTP — ошибка валидации.
- Выбор делает ядро (служба) при валидации запроса, интерфейс показывает тот же результат той же политикой.

### Профиль XHTTP

- Ссылка: `type=xhttp` (синоним `splithttp`), `path`, `host`, `mode` (`auto|packet-up|stream-up|stream-one`),
  `extra` — JSON. Поля хранятся типизированно (`xhttpPath`, `xhttpHost`, `xhttpMode`, `xhttpExtra`).
- `extra` разбирается в типизированную модель `XhttpExtraModel` с полями `SplitHTTPConfig` Xray v26.10.31;
  неизвестные ключи отклоняются при импорте понятной ошибкой. Не поддерживаются и отклоняются: `downloadSettings`
  (раздельные каналы: это целый второй `streamSettings`), `h2Params`, вложенный `extra`. Диапазоны —
  `N` или `N-M`; заголовки — без управляющих символов, без `Host`. Служба повторяет ту же проверку.
- `flow` (Vision) Xray допускает только для TCP+TLS/REALITY, поэтому для XHTTP он не передаётся.
- `StableId` профиля не меняется для существующих типов; для XHTTP в ключ добавляются его поля.
- `mode` и `extra` — известные ключи только для XHTTP; у gRPC `mode` по-прежнему остаётся в `extraQuery`.

### XMUX по умолчанию (раздел «Xray» в настройках)

- По документации XHTTP, если XMUX не задан вовсе, Xray берёт `maxConcurrency` 16-32, `hMaxRequestTimes` 600-900,
  `hMaxReusableSecs` 1800-3000; если задано хоть одно поле, остальные значений по умолчанию не получают.
- Эти значения — константы `XhttpPolicy` и явно пишутся в конфиг Xray, если в `extra` профиля нет `xmux`.
  Пользователь может изменить их в разделе «Xray» (state.json); `xmux` из ссылки всегда важнее.

### Контракт ядра

- `EngineContract.Version` 2 → 3: в `EngineProfile` — `engine`, `xhttpPath`, `xhttpHost`, `xhttpMode`,
  `xhttpExtra`; в запросе — `xmux`. Запись — в ADR-0001.
- Порт `IOutboundEnginePort` (Application) — внешний процесс выхода: записать и проверить конфиг
  (`xray run -test`), запустить, остановить, событие выхода. `SingBoxVpnEngine` запускает Xray, ждёт его порт,
  затем sing-box с `socks`-выходом; выход любого процесса останавливает оба.
- `xray.exe` в службе ищется только в каталоге установки, как `sing-box.exe`. Журнал Xray маскируется
  `LogRedactor`, источник — `xray/stdout`, `xray/stderr`.

### Поставка

- `release.yml`: `XRAY_VERSION` 26.10.31 и `XRAY_SHA256` архива `Xray-windows-64.zip`; из архива берутся
  `xray.exe` и `LICENSE` (MPL-2.0, ставится как `xray-LICENSE.txt`). `geoip.dat`/`geosite.dat` не нужны:
  маршрутизации в Xray нет.
- `Build.ps1` копирует `xray.exe` и лицензию как runtime-ассеты; установщик требует `xray.exe`.

## Вне scope

- VLESS Encryption (`mlkem768x25519plus`) — отдельная задача; ссылки с `encryption` ≠ `none` по-прежнему отклоняются.
- `downloadSettings` XHTTP, REALITY `pqv` (`mldsa65Verify`), `spx`, ECH, gRPC `multiMode` в Xray.
- Отображение версии Xray в окне «О программе».

## Последствия и риски

- Лишний локальный переход (SOCKS5 на loopback) — небольшие накладные расходы; UDP идёт через SOCKS5 UDP ASSOCIATE.
- Установщик больше примерно на 35 МБ; `xray.exe` иногда ложно определяется антивирусами.
- REALITY-профили после обновления пойдут через Xray: поведение меняется для существующих пользователей
  (ручной откат — `engine: sing-box` у профиля).
- Подписки начнут импортировать узлы XHTTP, ранее пропускавшиеся.
- Xray 26.x отказывается запускать VLESS без TLS/REALITY к публичному адресу («vless without TLS … is prohibited»).
  Профиль `security=none` с движком Xray (явным или XHTTP в режиме «авто») не подключится: ошибка `xray run -test`
  показывается пользователю. Политику Xray клиент не дублирует.
- Нужна ручная проверка на реальных серверах (чек-лист в SERVICE-INSTALL-CHECKLIST.md).

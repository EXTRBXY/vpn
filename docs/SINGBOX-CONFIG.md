# Генерация конфигурации sing-box

Источник: `src/NothingVpn.Infrastructure/SingBox/SingBoxConfigGenerator.cs`. Решения о маршрутизации вынесены в
политики `src/NothingVpn.Domain/Policies/*`. Поддерживаемая версия sing-box закреплена в
`.github/workflows/release.yml` (`SING_BOX_VERSION`, сейчас 1.13.21). Перед изменением полей сверяйтесь с документацией
sing-box именно этой версии: генератор использует формат 1.12+ (DNS-серверы с `type`, `route.default_domain_resolver`,
rule `action`).

Тесты, фиксирующие форму конфигурации: `tests/NothingVpn.Application.Tests/SingBoxConfigGeneratorTests.cs`,
`SecurityRegressionTests.cs`, `DomainPoliciesTests.cs`; выход через Xray — `XrayConfigGeneratorTests.cs`.

## Жизненный цикл файла

- Путь: `%LOCALAPPDATA%\NothingVpn.Tray\configs\{profileId}.{mode}.json`.
- Создаётся перед каждым подключением, проверяется `sing-box check -c`, запускается `sing-box run -c`.
- Удаляется при остановке, при самопроизвольном выходе sing-box и при откате неудачного подключения.
- Файл содержит UUID и ключи REALITY **в открытом виде** на время сессии.
- Сериализация: `snake_case`, `null`-поля опускаются.

## Режимы

`ConnectionMode` (`ConnectionPolicy.Modes`): `proxy` (по умолчанию для любого неизвестного значения), `tun`, `tun_apps`.
Все enum-настройки попадают в конфиг канонической строкой `ToValue()` — той же, что в state.json (STORAGE.md);
имя файла конфига тоже использует её: `{profileId}.tun_apps.json`.

| | `proxy` | `tun` | `tun_apps` |
| --- | --- | --- | --- |
| Inbound | `mixed` `127.0.0.1:{LocalMixedPort}` (1080) | `tun` | `tun` |
| `route.final` | `proxy` | `proxy` | `direct` |
| `auto_detect_interface` | `false` | `true` | `true` |
| Системный прокси WinINET | включается | нет | нет |
| Права администратора у процесса sing-box | нет | да | да (sing-box запускает служба LocalSystem; интерфейсу права не нужны) |
| `strict_route` | — | настройка пользователя | всегда `false` |
| DNS hijack | — | да | да |
| QUIC и IPv6 → proxy | — | да (после пользовательских правил) | нет |
| Известные DoH-домены → proxy | — | да | нет |
| DNS detour `proxy` | разрешён | **принудительно**, если strict_route | запрещён (всегда direct) |

## Outbounds

Всегда три: `proxy` (vless), `direct`, `block`.

`proxy`: `server`, `server_port`, `uuid`, `flow`, `tls`, `transport`, при DNS через VPN — `domain_resolver: bootstrap-local`.

- `security=none` → без `tls`.
- `tls` → `server_name` (SNI или host), `alpn`, `utls.fingerprint` (если задан `fp`).
- `reality` → то же + `reality.public_key`, `reality.short_id`.
- Транспорт: `tcp` — нет; `ws` — `path` (по умолчанию `/`) и заголовок `Host`; `grpc` — `service_name` (по умолчанию `default`).

Поддерживаемые VLESS-ссылки (`Profile/VlessLinkParser.cs`): `encryption=none`, `security ∈ {tls, reality, none}`,
`type ∈ {tcp, ws, grpc, xhttp}` (`splithttp` — синоним `xhttp`); для REALITY обязательны `pbk`, `sid`, `sni`.
Неизвестные параметры сохраняются в `ExtraQuery` и возвращаются при экспорте ссылки, но в конфигурацию **не попадают**.

### Выход через Xray (ADR-0002)

Если `OutboundEnginePolicy.Resolve` выбрал для профиля Xray (XHTTP всегда, REALITY в режиме «авто», либо явный
выбор пользователя), `proxy` — не `vless`, а локальный SOCKS5 к процессу Xray:

```json
{ "type": "socks", "tag": "proxy", "server": "127.0.0.1", "server_port": <порт>, "version": "5",
  "username": "<одноразовый>", "password": "<одноразовый>" }
```

- Порт и учётные данные новые на каждое подключение (`XrayOutboundPort`); `domain_resolver` не ставится — имя
  сервера разрешает сам Xray.
- Всё остальное (inbounds, DNS, rule-set, правила) — то же, что для sing-box-выхода; в TUN добавляются два правила
  исключения (см. ниже).
- Конфиг Xray — `XrayConfigGenerator`: `socks`-inbound на `127.0.0.1` с паролем и `udp`, один `vless`-outbound
  (`raw`/`ws`/`grpc`/`xhttp`, `tlsSettings`/`realitySettings`), `log.access: none`. Файл
  `configs\{profileId}.xray.json`, проверяется `xray run -test -c`, удаляется вместе с конфигом sing-box.
- XHTTP: `xhttpSettings.path/host/mode` и `extra` из профиля; если в `extra` нет `xmux`, пишется XMUX из настроек
  (по умолчанию — значения, которые Xray берёт сам: 16-32 / 0 / 0 / 600-900 / 1800-3000).

## Inbound TUN

| Поле | Значение |
| --- | --- |
| `interface_name` | `{TunInterfaceName}-{первые 6 символов profileId}` |
| `address` | IPv4: заданный CIDR или детерминированный `198.18.X.Y/30` из хеша profileId (`auto`, пусто и legacy `172.19.0.1/30` → auto); IPv6: `fdfe:dcba:9876:{hextet}::1/126` из того же хеша |
| `mtu` | `TunSettingsPolicy.NormalizeMtu`: ≤0 или legacy 9000 → 1500, иначе clamp 576..9000 |
| `auto_route` | настройка (по умолчанию `true`) |
| `strict_route` | `TunAppsPolicy.UseStrictRoute` |
| `stack` | `system` / `mixed` / `gvisor`; `TunStack.Default` (в state.json `""`) → `system` на Windows (`TunInterfacePolicy.SingBoxStack`) |

Хеш адресов — `n = n*31 + char` по строке profileId. Изменение формулы меняет адреса интерфейсов у всех пользователей.

## Правила маршрутизации

Порядок важен: sing-box применяет первое совпавшее правило.

### `proxy`

1. `sniff`
2. `protocol: stun` → `reject`
3. Пользовательские rule-set (в порядке списка, только включённые, уникальные теги): `rule_set: [tag]` → `direct` | `block`

`default_domain_resolver = doh`, если DNS-режим DoH.

### `tun` и `tun_apps`

1. `sniff`
2. `protocol: dns` → `hijack-dns`
3. `ip_is_private` → `direct`
4. Домен сервера профиля (если это не IP) → `direct` — иначе туннель зациклится на себя
4а. Только с выходом через Xray: `process_path: [путь xray.exe]` → `direct`, затем `ip_cidr: [IP сервера/32|128]`
    → `direct` (если адрес сервера — IP). Иначе соединения Xray к серверу вернулись бы в TUN.
5. Только `tun`: известные DoH-домены (`TunRoutingPolicy.KnownSecureDnsDomains`) → `proxy`
6. `protocol: stun` → `reject`
7. Пользовательские rule-set → `direct` | `block`
8. Только `tun`: `protocol: quic` → `proxy`, затем `ip_version: 6` → `proxy`
9. Только `tun_apps`: `process_path: [...]` → `proxy`, затем `process_name: [...]` → `proxy`

`default_domain_resolver = bootstrap-local`.

Пункт 8 стоит **после** пользовательских правил намеренно: иначе direct/block-списки не срабатывали для HTTP/3 и IPv6
(тест `Build_FullTun_UserRuleSetsTakePriorityOverIpv6AndQuicFallbacks`). Пункт 9 — после пользовательских правил
(тест `Build_TunApps_UserRuleSetsBeforeProcessPath`).

`process_name` дублирует `process_path` именами файлов: это расширяет совпадение на любые `.exe` с тем же именем.

## Rule-set

Каждый включённый пользовательский rule-set превращается в
`{ "type": "local", "tag", "path": "<RuleSetsDir>\\<file>.srs", "format": "binary" }` и одно route-правило.
Имя файла — только имя, без каталогов, `..` и абсолютных путей, обязательно `.srs`
(`RuleSetPolicyValidator.IsSafeRuleSetFileName` и дублирующая проверка в генераторе). Действие — `direct` или `block`.

Встроенные наборы (`RuleSets/BuiltinGeositeRuleSets.cs`) добавляются в состояние выключенными при первой загрузке:

| BuiltinId | Tag | Файл | Действие по умолчанию |
| --- | --- | --- | --- |
| `sing-geosite:category-ru` | `geosite-category-ru` | `geosite-category-ru.srs` | direct |
| `sing-geosite:category-ru-ads` | `geosite-category-ru-ads` | `geosite-category-ru@ads.srs` | block |
| `sing-geoip:ru` | `geoip-ru` | `geoip-ru.srs` | direct |

Скачиваются с `raw.githubusercontent.com/SagerNet/sing-geosite|sing-geoip/rule-set/…` с поддержкой `ETag`.

## DNS

Решение принимает `RuleSetRoutingPolicy.Evaluate` + `DnsDetourPolicy` + `TunBootstrapPolicy`.

Блок `dns` не создаётся вовсе, если режим `proxy`, DNS системный и нет включённых rule-set.

### DoH (`DnsMode = doh`)

Серверы:

- `bootstrap-local` (`type: local`, `prefer_go: false`) — если TUN, или нужен split DNS, или detour = `proxy`;
- `doh` (`type: https`, `server` = IP из настроек, порт 443, `path`, `tls.server_name` = SNI, `detour`).

Правила:

- только TUN: домен сервера профиля → `bootstrap-local`;
- включённые `direct`-наборы без IP-условий в содержимом `.srs` → `bootstrap-local` (split DNS).

Перед генерацией `SingBoxPort` читает структуру включённых direct-файлов через `SrsRuleSetInspector`
(SRS v1–4, формат sing-box 1.13.21). Название файла, тег и `BuiltinId` не определяют тип списка.
В службе проверяются уже полученные байты, сохранённые под служебными именами с хешем; отдельные метаданные
в `state.json` и IPC не передаются. Проверка повторяется при каждом подключении, поэтому замена файла учитывается сразу.

IP-списки и смешанные списки с IP-условиями остаются в `route.rules`, но целиком исключаются из DNS-правил.
Иначе sing-box сначала запрашивает локальный DNS для проверки IP ответа и, если адрес не подходит, обращается
к основному DoH. Доменным спискам (включая AdGuard) split DNS доступен независимо от имени, даже `geoip-*`.
Доменная часть смешанного списка использует основной DNS; его правила маршрутизации сохраняются полностью.
Ручные доменные исключения продолжают получать отдельные правила `bootstrap-local` с прежним приоритетом.

`final: doh`; в TUN — `reverse_mapping: true`, `strategy: ipv4_only`.

### Системный DNS (`DnsMode = system`)

Один сервер `bootstrap-local`, `final: bootstrap-local`, `strategy: ipv4_only`, правило для домена профиля,
`reverse_mapping` в TUN.

### Detour DoH

`ResolveDnsDetour` в генераторе:

1. `tun_apps` → без detour (direct). DNS через VPN ломает модель split-tunnel.
2. `proxy` → `proxy`, если пользователь выбрал «Через VPN».
3. `tun` → если эффективный `strict_route` включён, detour **всегда** `proxy`, даже если пользователь выбрал direct
   (`TunBootstrapPolicy.ResolveSingBoxDohDetour`).

При detour `proxy` outbound `proxy` получает `domain_resolver: bootstrap-local`, чтобы адрес VLESS-сервера не
резолвился через сам туннель.

## Пресеты DoH

`DnsPolicy.ApplyPreset` / `StateToPresetIndex`: Cloudflare `1.1.1.1` / `cloudflare-dns.com`; Google `8.8.8.8` /
`dns.google` (значение по умолчанию); Quad9 `9.9.9.9` / `dns.quad9.net`; AdGuard `94.140.14.14` / `dns.adguard.com`.
Путь — `/dns-query`.

## Уровень журнала

`AppStateModel.SingBoxLogLevel` → `log.level`; допустимы `trace|debug|info|warn|error|fatal|panic` (`warning` →
`warn`), неизвестное → `info` (`SingBoxLogLevelPolicy`). По умолчанию в состоянии `warn`.

## Что генератор делает сам, а что должно быть в политике

Генератор принимает `VpnProfile` и `AppStateModel`, уже нормализованные `AppStateNormalizer`. С шага 6 их собирает
ядро (`SingBoxVpnEngine`) из типизированного `EngineConnectRequest` (`EngineRequestMapper.ToProfile`/`ToState`) и
нормализует перед `WriteConfig`, не полагаясь на нормализацию интерфейса; см. ARCHITECTURE.md, «Граница
"интерфейс ↔ ядро"». Генератор и итоговый JSON от этого не изменились: `EngineContractTests` сверяет конфиг, построенный
через JSON запроса, с конфигом из состояния напрямую на всей матрице эталонов. Сам генератор значения не нормализует:

- стек, CIDR, имя интерфейса, MTU, DNS-режим и DoH-поля, уровень журнала, пути процессов — из нормализованного
  состояния как есть;
- значения, производные от Id профиля (суффикс имени интерфейса, адреса `198.18.x.y/30` и `fdfe:dcba:9876:…/126`), и
  «стек по умолчанию» (`""` → `system` на Windows) — `TunInterfacePolicy`;
- проверяет то, без чего конфиг некорректен: пустой DoH SNI, имя файла и действие rule-set, `tun_apps` без приложений.

Новое поведение добавляйте политикой в Domain, а не условием в генераторе.

Отличия от поведения до шага 3 — только на входах, которые клиент сам не записывает (для них итоговый JSON на
реальных состояниях не изменился, это проверяют `SingBoxConfigGoldenTests`):

- пустой DoH-сервер → `8.8.8.8` (`DnsPolicy.DefaultDohServer`) вместо `1.1.1.1`; UI не сохраняет DoH без сервера;
- неизвестный `dnsMode` → `doh` (как в `DnsPolicy`), раньше генератор считал его `system`;
- имя интерфейса с недопустимыми символами → `NothingVpn`, длиннее 64 символов — обрезается (как в
  `TunSettingsPolicy`), раньше генератор только обрезал пробелы;
- пути процессов дополнительно проходят `IPathPolicyPort` (полный путь, `.exe`) — это и раньше делал
  `VpnConnectionService`, генератор теперь не дублирует их обрезку и удаление дублей.

## Ручные доменные исключения

Включённые `domainExceptions` формируют правила `action: route`, `outbound: direct` во всех режимах.
Без поддоменов используется `domain: [имя]`, с поддоменами — `domain_suffix: [имя]` (без начальной точки:
совпадают сам домен и его поддомены). Они стоят после служебных head-правил/STUN и **перед всеми `.srs`**,
QUIC/IPv6-фолбэками и правилами приложений. Отключённые записи не создают правил.
При DoH для тех же matcher-ов перед правилами `.srs` добавляется DNS-маршрут на `bootstrap-local`;
локальный DNS-сервер создаётся даже в proxy без `.srs`. В системном режиме используется существующий
локальный DNS; TUN сохраняет `reverse_mapping`. Поддержка `domain`/`domain_suffix` проверена по
[route](https://github.com/SagerNet/sing-box/blob/v1.13.21/option/rule.go) и
[DNS](https://github.com/SagerNet/sing-box/blob/v1.13.21/option/rule_dns.go) sing-box 1.13.21.
Изменения применяются после сохранения и переподключения. Сторонние CDN-домены сайта добавляются отдельно.

Клиент канала передаёт `domainExceptions` при пересборке `EngineConnectRequest` вместе с содержимым `.srs`.
Ручные исключения не требуют файлов rule-set; их route- и DNS-правила должны присутствовать в конфиге службы
даже при полностью выключенных списках. Этот путь покрыт интеграционными тестами `ServiceChannelTests`.

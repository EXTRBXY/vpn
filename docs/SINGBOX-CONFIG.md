# Генерация конфигурации sing-box

Источник: `src/NothingVpn.Infrastructure/SingBox/SingBoxConfigGenerator.cs`. Решения о маршрутизации вынесены в
политики `src/NothingVpn.Domain/Policies/*`. Поддерживаемая версия sing-box закреплена в
`.github/workflows/release.yml` (`SING_BOX_VERSION`, сейчас 1.13.21). Перед изменением полей сверяйтесь с документацией
sing-box именно этой версии: генератор использует формат 1.12+ (DNS-серверы с `type`, `route.default_domain_resolver`,
rule `action`).

Тесты, фиксирующие форму конфигурации: `tests/NothingVpn.Application.Tests/SingBoxConfigGeneratorTests.cs`,
`SecurityRegressionTests.cs`, `DomainPoliciesTests.cs`.

## Жизненный цикл файла

- Путь: `%LOCALAPPDATA%\NothingVpn.Tray\configs\{profileId}.{mode}.json`.
- Создаётся перед каждым подключением, проверяется `sing-box check -c`, запускается `sing-box run -c`.
- Удаляется при остановке, при самопроизвольном выходе sing-box и при откате неудачного подключения.
- Файл содержит UUID и ключи REALITY **в открытом виде** на время сессии.
- Сериализация: `snake_case`, `null`-поля опускаются.

## Режимы

`ConnectionPolicy`: `proxy` (по умолчанию для любого неизвестного значения), `tun`, `tun_apps`.

| | `proxy` | `tun` | `tun_apps` |
| --- | --- | --- | --- |
| Inbound | `mixed` `127.0.0.1:{LocalMixedPort}` (1080) | `tun` | `tun` |
| `route.final` | `proxy` | `proxy` | `direct` |
| `auto_detect_interface` | `false` | `true` | `true` |
| Системный прокси WinINET | включается | нет | нет |
| Права администратора | нет | да | да |
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
`type ∈ {tcp, ws, grpc}`; для REALITY обязательны `pbk`, `sid`, `sni`. Неизвестные параметры сохраняются в
`ExtraQuery` и возвращаются при экспорте ссылки, но в конфигурацию **не попадают**.

## Inbound TUN

| Поле | Значение |
| --- | --- |
| `interface_name` | `{TunInterfaceName}-{первые 6 символов profileId}` |
| `address` | IPv4: заданный CIDR или детерминированный `198.18.X.Y/30` из хеша profileId (`auto`, пусто и legacy `172.19.0.1/30` → auto); IPv6: `fdfe:dcba:9876:{hextet}::1/126` из того же хеша |
| `mtu` | `TunSettingsPolicy.NormalizeMtu`: ≤0 или legacy 9000 → 1500, иначе clamp 576..9000 |
| `auto_route` | настройка (по умолчанию `true`) |
| `strict_route` | `TunAppsPolicy.UseStrictRoute` |
| `stack` | `system` / `mixed` / `gvisor`; пусто → `system` на Windows |

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
- включённые `direct`-наборы, кроме `geoip-*` (они матчат адреса, а не имена) → `bootstrap-local` (split DNS).

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

`AppState.SingBoxLogLevel` → `log.level`; допустимы `trace|debug|info|warn|error|fatal|panic` (`warning` → `warn`),
неизвестное → `info`. По умолчанию в состоянии `warn`.

## Что генератор делает сам, а что должно быть в политике

Генератор повторно нормализует то, что уже нормализует Domain: стек TUN, CIDR, имя файла rule-set, пути процессов,
уровень журнала, значения DNS по умолчанию (`1.1.1.1` против `8.8.8.8` в политике). При рефакторинге оставьте одну
точку нормализации и сохраните итоговый JSON неизменным (сравнивайте вывод до/после на одинаковых входах).

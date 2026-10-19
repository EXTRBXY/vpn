# Хранение данных

Всё пользовательское состояние лежит в `%LOCALAPPDATA%\NothingVpn.Tray\` (`Store/AppPaths.cs`; если переменная
недоступна — `<exe>\.data`). Имя каталога осталось от WinForms-клиента. Его используют установщик (вопрос об удалении
данных при деинсталляции) и все существующие установки — переименование требует миграции.

```text
%LOCALAPPDATA%\NothingVpn.Tray\
  profiles.json         профили VLESS               (DPAPI)
  subscriptions.json    подписки                    (DPAPI)
  state.json            настройки и runtime-флаги   (DPAPI)
  *.json.bak            предыдущая версия каждого файла
  configs\              временные конфиги sing-box (открытый текст, удаляются после сессии)
  rulesets\             *.srs — встроенные и импортированные rule-set
  logs\                 создаётся в путях, но не используется
```

## Формат файлов (`Security/DpapiJsonFile.cs`)

- Содержимое: `"NV1"` + `ProtectedData.Protect(utf8(json), entropy: null, CurrentUser)`.
- JSON: `System.Text.Json`, `camelCase`, с отступами. Чтение чувствительно к регистру имён; неизвестные поля
  игнорируются, отсутствующие получают значения по умолчанию из класса.
- Файл без префикса `NV1` считается legacy-открытым JSON: читается и сразу перешифровывается.
- Запись атомарная: временный файл рядом → `File.Replace(tmp, path, path.bak)` (или `Move`, если файла ещё нет).
- Чтение: при ошибке основного файла берётся `.bak`, восстанавливается на место основного и регистрируется в
  `StorageIssueRegistry`; WPF показывает об этом сообщение при старте. Если не читается и `.bak` — используются
  значения по умолчанию (то есть **пустые** профили/настройки), а `.bak` остаётся на диске.
- Блокировки — только внутри процесса (`lock` на полный путь). Межпроцессной блокировки нет; два экземпляра
  клиента не работают одновременно благодаря мьютексу single-instance.
- DPAPI CurrentUser: файлы нельзя прочитать под другим пользователем Windows или перенести на другую машину.

## Схемы

`state.json` и `profiles.json` сериализуют Application-модели напрямую; имена полей зафиксированы атрибутами
`[JsonPropertyName]` на `AppStateModel`, `UserRuleSetModel`, `ProxySettingsSnapshotModel`, `VpnProfile`. Изменение
значения атрибута равносильно потере данных у пользователей; переименование C#-свойства формат не меняет.
`subscriptions.json` хранится через плоский DTO `SubscriptionRecord` (имена — camelCase свойств).

### `profiles.json` — `List<VpnProfile>` (`Application/Models/VpnProfile.cs`)

`id`, `subscriptionId`, `name`, `uuid`, `host`, `port`, `type`, `security`, `encryption`, `sni`, `alpn[]`,
`fingerprint`, `flow`, `realityPublicKey`, `realityShortId`, `wsPath`, `wsHost`, `grpcServiceName`, `extraQuery{}`.

`id` — первые 16 hex-символов SHA-256 от
`uuid|host|port|type|security|sni|flow|pbk|sid|wsPath|wsHost|grpcServiceName` (`VlessLinkParser.StableId`).
Имя не входит в Id. На Id завязаны: `state.activeProfileId`, синхронизация подписок (добавить/обновить/удалить),
имя конфига и имя TUN-интерфейса, адреса TUN. Редактирование профиля, меняющее эти поля, создаёт новый Id; контроллер
удаляет старый профиль.

### `subscriptions.json` — `List<SubscriptionRecord>` (`Store/JsonSubscriptionStore.cs`)

`id` (GUID `N`), `name`, `url` (только https), `enabled`, `lastSyncUtc`, `lastError`, `updateIntervalHours`
(по умолчанию 24, допустимо 1..720), `upload`, `download`, `total`, `expireUtc`.

Профили из подписки помечаются `subscriptionId`; в UI их нельзя редактировать, они заменяются при обновлении.
Удаление подписки удаляет и её профили.

### `state.json` — `AppStateModel` (`Application/Models/AppStateModel.cs`)

| Поле | По умолчанию | Смысл |
| --- | --- | --- |
| `activeProfileId` | null | выбранный профиль (также «был подключён») |
| `mode` | `proxy` | `proxy` / `tun` / `tun_apps` |
| `tunAppProcessPaths` | `[]` | полные пути `.exe` для `tun_apps`; существование файла не требуется |
| `userRuleSets[]` | 3 встроенных, выключены | `tag`, `name`, `fileName`, `enabled`, `action` (`direct`/`block`), `builtinId`, `remoteEtag`, `lastDownloadedUtc` |
| `localMixedPort` | 1080 | порт `mixed`-inbound; в UI не редактируется |
| `proxyWasEnabledByUs` | false | **runtime-флаг**: системный прокси включил клиент |
| `previousProxySettings` | null | **runtime**: снимок WinINET до включения (`proxyEnable`, `proxyServer`, `proxyOverride`) |
| `proxyOverride` | `localhost;127.*;10.*;192.168.*;172.16.*` | исключения WinINET, ≤ 2048 символов |
| `tunInterfaceName` | `NothingVpn` | ≤ 64 символа, без `\/:*?"<>|` |
| `tunAddressCidr` | `auto` | IPv4 CIDR /1..32 или `auto` |
| `tunMtu` | 1500 | 576..9000 (9000 — legacy-маркер, читается как 1500) |
| `tunStack` | `""` | `""` (system) / `system` / `mixed` / `gvisor` |
| `tunAutoRoute`, `tunStrictRoute` | true, true | |
| `dnsMode` | `doh` | `doh` / `system` |
| `dohServer`, `dohPath`, `dohSni` | `8.8.8.8`, `/dns-query`, `dns.google` | |
| `dnsDetour` | `direct` | `direct` / `proxy`; в `tun_apps` всегда `direct` |
| `debugLogs` | false | использовал только WinForms |
| `singBoxLogLevel` | `warn` | |
| `closeBehavior` | `tray` | `tray` / `exit` |
| `updateDismissedModalForTag` | null | тег, о котором уже уведомили |
| `lastRecordedAppSemver` | null | версия при последнем запуске |
| `updateLastCheckUtc` | null | время последней проверки обновлений |

### Enum-поля в `state.json`

`mode`, `tunStack`, `dnsMode`, `dnsDetour`, `singBoxLogLevel`, `closeBehavior` и `userRuleSets[].action` в модели —
enum'ы, а на диске — прежние строки из таблицы выше (`tunStack` по умолчанию — пустая строка `""`, она отличается от
явного `system`). Строки задаёт `SettingValueMap<T>` в Domain-политиках; сериализацию — толерантные конвертеры
`Application/Serialization/SettingValueJsonConverter.cs`, подключённые атрибутом `[JsonConverter]` к свойствам моделей:

- запись — всегда каноническая строка (неопределённое числовое значение enum'а пишется как значение по умолчанию);
- чтение не зависит от регистра и пробелов по краям (`" TUN "` → `tun`, `"Warning"` → `warn`);
- неизвестная строка, `null` или значение другого типа (число, объект, массив) дают значение по умолчанию поля, а
  **не исключение**: стандартный `JsonStringEnumConverter` бросил бы `JsonException`, `DpapiJsonFile` счёл бы файл
  повреждённым и пользователь потерял бы все настройки. Для `singBoxLogLevel` значение по умолчанию при неизвестной
  строке — `info` (как и до перехода на enum), при отсутствии поля — `warn`; для `action` — `direct`.

Тесты: `SettingValueSerializationTests`; формат по-прежнему фиксируют `StorageFormatCompatibilityTests`.

`StateStorePort.Load` ничего не пишет: недостающие встроенные rule-set добавляются только в возвращаемую модель.
На диск их дописывает явная миграция `SaveMissingBuiltinRuleSets` при старте (`ApplicationServicesFactory`, под локом
`AppStateStore`); кроме того, они попадают в файл при любом следующем сохранении состояния.

Все записи `state.json` в процессе идут через один `AppStateStore`: чтение-изменение-запись конкретных полей под
одним локом, целиком устаревший объект не сохраняется.

Runtime-флаги `proxyWasEnabledByUs` / `previousProxySettings` пишет только `VpnConnectionService` (сразу после
включения системного прокси и при отключении/откате/восстановлении). `SettingsService.UpdateState` переносит их из
свежей копии и изменить не может. Если бы их перетёрли старой копией состояния, после отключения системный прокси
остался бы включённым или был бы восстановлен неверно.

## Значения по умолчанию

Один источник — константы Domain-политик (`ConnectionPolicy`, `DnsPolicy`, `TunSettingsPolicy`,
`ProxyConnectionPolicy`, `SingBoxLogLevelPolicy`). `AppStateModel` и `DnsSettings`/`TunSettings`/
`ProxyConnectionSettings` ссылаются на них. Отсутствующее в файле поле получает значение по умолчанию модели.

Исключение — профиль: у `VpnProfile` пустые `id` и `name` по умолчанию (у удалённого legacy-класса были новый GUID и
`Unnamed`). Клиент всегда записывает оба поля, поэтому для файлов, созданных клиентом, разницы нет.

## Каталог rule-set

- Импорт пользовательского `.srs` копирует файл в `rulesets\`; при совпадении имени добавляется суффикс
  `-xxxxxxxx`. Тег — `user-ruleset-<12 hex>`.
- Встроенные наборы скачиваются во временный файл и атомарно заменяют целевой.
- Удаление файла выполняется только после успешного сохранения состояния, где набор уже выключен/удалён.

## Прочие места на диске

- `%TEMP%\NothingVpn\NothingVpnSetup-x.y.z.exe` — скачанный установщик; кэш чистится при каждом старте.
- `wpf-smoke-error.txt` рядом с exe — только при `--smoke-test`.
- Реестр: `HKCU\...\Internet Settings` (`ProxyEnable`, `ProxyServer`, `ProxyOverride`) — системный прокси;
  `HKCU\...\Run\NothingVpn` — автозапуск, если выбран при установке.

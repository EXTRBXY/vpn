# Документация Nothing VPN

| Документ | О чём |
| --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Слои и проекты, composition root, служба и установка, сценарии: подключение, подписки, обновления, настройки, потоки, журнал |
| [SINGBOX-CONFIG.md](SINGBOX-CONFIG.md) | Как строится конфигурация sing-box для каждого режима: inbounds, outbounds, порядок правил, DNS, rule-set |
| [STORAGE.md](STORAGE.md) | Каталог данных, формат DPAPI-файлов, схемы `profiles.json` / `subscriptions.json` / `state.json` |
| [SECURITY.md](SECURITY.md) | Защита секретов, валидация входных данных, привилегии, известные риски |
| [TESTING.md](TESTING.md) | Состав тестов, запуск, smoke-test, что не покрыто, что добавить перед рефакторингом |
| [adr/0001-privileged-service.md](adr/0001-privileged-service.md) | Решение: служба Windows вместо повышения клиента; протокол, установка, переход со старых версий |
| [REFACTORING.md](REFACTORING.md) | Инварианты, найденные риски, технический долг и предлагаемый порядок рефакторинга |
| [UPDATE-HANDOFF.md](UPDATE-HANDOFF.md) | Запуск установщика обновления из интерфейса: порядок, защита от наследования прав, история помощника |
| [SERVICE-INSTALL-CHECKLIST.md](SERVICE-INSTALL-CHECKLIST.md) | Ручная проверка установки службы, подключения без UAC, обновления, удаления и перехода с 0.6.8 |
| [UI-REGRESSION-CHECKLIST.md](UI-REGRESSION-CHECKLIST.md) | Исправления WPF UI и ручная проверка перед релизом |
| [WPF-MIGRATION.md](WPF-MIGRATION.md) | План перехода с WinForms на WPF и критерии удаления `NothingVpn.Tray` |
| [../DEVELOPMENT.md](../DEVELOPMENT.md) | Сборка, публикация, установщик, CI и релизы |

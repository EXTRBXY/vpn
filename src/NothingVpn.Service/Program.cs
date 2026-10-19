using System.ServiceProcess;
using NothingVpn.Infrastructure.Installation;
using NothingVpn.Service;

// Установщик запускает exe службы и как обычный процесс — от имени исходного пользователя, чтобы убрать его старую
// per-user установку (ADR-0001, шаг 7б).
if (args.Length > 0 && string.Equals(args[0], LegacyUserInstallMigration.Switch, StringComparison.OrdinalIgnoreCase))
{
    return LegacyUserInstallMigration.RunForCurrentUser(
        enableAutorun: args.Skip(1).Contains(LegacyUserInstallMigration.AutorunSwitch, StringComparer.OrdinalIgnoreCase));
}

// Иначе запускается только диспетчером служб: канал создаётся с владельцем SYSTEM, а клиент проверяет, что сервер —
// служба LocalSystem, поэтому запуск из терминала (даже администратора) клиенту бесполезен.
ServiceBase.Run(new NothingVpnWindowsService());
return 0;

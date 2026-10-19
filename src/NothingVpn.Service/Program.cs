using System.ServiceProcess;
using NothingVpn.Service;

// Запускается только диспетчером служб: канал создаётся с владельцем SYSTEM, а клиент проверяет, что сервер —
// служба LocalSystem, поэтому запуск из терминала (даже администратора) клиенту бесполезен.
ServiceBase.Run(new NothingVpnWindowsService());
